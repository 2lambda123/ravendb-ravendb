﻿using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Raven.NewClient.Client.Blittable;
using Raven.NewClient.Client.Connection;
using Raven.NewClient.Client.Data;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Raven.NewClient.Client.Http;

namespace Raven.NewClient.Client.Commands
{
    public class MultiGetCommand : RavenCommand<List<GetResponse>>
    {
        private readonly JsonOperationContext _context;
        private readonly HttpCache _cache;
        private readonly List<GetRequest> _commands;

        private string _baseUrl;

        public MultiGetCommand(JsonOperationContext context, HttpCache cache, List<GetRequest> commands)
        {
            _context = context;
            _cache = cache;
            _commands = commands;
        }

        public override HttpRequestMessage CreateRequest(ServerNode node, out string url)
        {
            _baseUrl = $"{node.Url}/databases/{node.Database}";

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post
            };

            var commands = new List<DynamicJsonValue>();
            foreach (var command in _commands)
            {
                string requestUrl;
                var cacheKey = GetCacheKey(command, out requestUrl);

                long cachedEtag;
                BlittableJsonReaderObject cachedResponse;
                using (_cache.Get(_context, cacheKey, out cachedEtag, out cachedResponse))
                {
                    var headers = new DynamicJsonValue();
                    if (cachedEtag != 0)
                        headers["If-None-Match"] = $"\"{cachedEtag}\"";

                    foreach (var header in command.Headers)
                        headers[header.Key] = header.Value;
                    commands.Add(new DynamicJsonValue
                    {
                        [nameof(GetRequest.Url)] = $"/databases/{node.Database}{command.Url}",
                        [nameof(GetRequest.Query)] = $"{command.Query}",
                        [nameof(GetRequest.Method)] = command.Method,
                        [nameof(GetRequest.Headers)] = headers,
                        [nameof(GetRequest.Content)] = command.Content
                    });
                }
            }

            request.Content = new BlittableJsonContent(stream =>
            {
                using (var writer = new BlittableJsonTextWriter(_context, stream))
                {
                    writer.WriteStartArray();
                    var first = true;
                    foreach (var command in commands)
                    {
                        if (first == false)
                            writer.WriteComma();
                        first = false;
                        _context.Write(writer, command);
                    }
                    writer.WriteEndArray();
                }
            });

            url = $"{_baseUrl}/multi_get";

            return request;
        }

        private string GetCacheKey(GetRequest command, out string requestUrl)
        {
            requestUrl = $"{_baseUrl}{command.UrlAndQuery}";

            return $"{command.Method}-{requestUrl}";
        }

        public override async Task ProcessResponse(JsonOperationContext context, HttpCache cache, RequestExecuterOptions options,
            HttpResponseMessage response, string url)
        {
            JsonOperationContext.ManagedPinnedBuffer buffer;
            var state = new JsonParserState();

            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var parser = new UnmanagedJsonParser(context, state, "multi_get/response"))
            using (_context.GetManagedBuffer(out buffer))
            {
                if (UnmanagedJsonParserHelper.Read(stream, parser, state, buffer) == false)
                    ThrowInvalidResponse();

                if (state.CurrentTokenType != JsonParserToken.StartObject)
                    ThrowInvalidResponse();

                var property = UnmanagedJsonParserHelper.ReadProperty(context, stream, parser, state, buffer);
                if (property != nameof(BlittableArrayResult.Results))
                    ThrowInvalidResponse();

                var i = 0;
                Result = new List<GetResponse>();
                foreach (var result in UnmanagedJsonParserHelper.ReadArray(context, stream, parser, state, buffer))
                {
                    var getResponse = ConvertToGetResponse(result);
                    var command = _commands[i];

                    MaybeSetCache(getResponse, command, options);
                    MaybeReadFromCache(getResponse, command);

                    Result.Add(getResponse);

                    i++;
                }

                if (UnmanagedJsonParserHelper.Read(stream, parser, state, buffer) == false)
                    ThrowInvalidResponse();

                if (state.CurrentTokenType != JsonParserToken.EndObject)
                    ThrowInvalidResponse();
            }
        }

        private void MaybeReadFromCache(GetResponse getResponse, GetRequest command)
        {
            if (getResponse.StatusCode != HttpStatusCode.NotModified)
                return;

            string requestUrl;
            var cacheKey = GetCacheKey(command, out requestUrl);

            long cachedEtag;
            BlittableJsonReaderObject cachedResponse;
            using (_cache.Get(_context, cacheKey, out cachedEtag, out cachedResponse))
            {
                getResponse.Result = cachedResponse;
            }
        }

        private void MaybeSetCache(GetResponse getResponse, GetRequest command, RequestExecuterOptions options)
        {
            if (getResponse.StatusCode == HttpStatusCode.NotModified)
                return;

            string requestUrl;
            var cacheKey = GetCacheKey(command, out requestUrl);

            if (options.ShouldCacheRequest(requestUrl) == false)
                return;

            var result = getResponse.Result as BlittableJsonReaderObject;
            if (result == null)
                return;

            var etag = getResponse.Headers.GetEtagHeader();
            if (etag.HasValue == false)
                return;

            using (var memoryStream = new MemoryStream()) // how to do it better?
            {
                result.WriteJsonTo(memoryStream);
                memoryStream.Position = 0;

                _cache.Set(cacheKey, etag.Value, _context.ReadForMemory(memoryStream, "multi_get/result"));
            }
        }

        private GetResponse ConvertToGetResponse(BlittableJsonDocumentBuilder builder)
        {
            var reader = builder.CreateReader();

            HttpStatusCode statusCode;
            if (reader.TryGet(nameof(GetResponse.StatusCode), out statusCode) == false)
                ThrowInvalidResponse();

            BlittableJsonReaderObject result;
            if (reader.TryGet(nameof(GetResponse.Result), out result) == false)
                ThrowInvalidResponse();

            BlittableJsonReaderObject headersJson;
            if (reader.TryGet(nameof(GetResponse.Headers), out headersJson) == false)
                ThrowInvalidResponse();

            var getResponse = new GetResponse
            {
                Result = result,
                StatusCode = statusCode,
            };

            foreach (var propertyName in headersJson.GetPropertyNames())
                getResponse.Headers[propertyName] = headersJson[propertyName].ToString();

            return getResponse;
        }

        public override void SetResponse(BlittableJsonReaderObject response, bool fromCache)
        {
            ThrowInvalidResponse();
        }

        public override bool IsReadRequest => false;
    }
}