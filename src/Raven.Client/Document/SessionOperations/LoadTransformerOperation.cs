using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Client.Connection;
using Raven.Client.Data;
using Raven.Json.Linq;
using Raven.Imports.Newtonsoft.Json.Linq;

namespace Raven.Client.Document.SessionOperations
{
    public class LoadTransformerOperation
    {
        private readonly InMemoryDocumentSessionOperations documentSession;
        private readonly string transformer;
        private readonly string[] ids;

        public LoadTransformerOperation(InMemoryDocumentSessionOperations documentSession, string transformer, string[] ids)
        {
            this.documentSession = documentSession;
            this.transformer = transformer;
            this.ids = ids;
        }

        public T[] Complete<T>(LoadResult loadResult)
        {
            foreach (var include in SerializationHelper.RavenJObjectsToJsonDocuments(loadResult.Includes))
            {
                documentSession.TrackIncludedDocument(include);
            }

            if (typeof(T).IsArray)
            {
                var arrayOfArrays = loadResult.Results
                                                   .Select(x =>
                                                   {
                                                       if (x == null)
                                                           return null;

                                                       var values = x.Value<RavenJArray>("$values").Cast<RavenJObject>();

                                                       var elementType = typeof(T).GetElementType();
                                                       var array = values.Select(value =>
                                                       {
                                                           EnsureNotReadVetoed(value);
                                                           return documentSession.ProjectionToInstance(value, elementType);
                                                       }).ToArray();
                                                       var newArray = Array.CreateInstance(elementType, array.Length);
                                                       Array.Copy(array, newArray, array.Length);
                                                       return newArray;
                                                   })
                                                   .Cast<T>()
                                                   .ToArray();

                return arrayOfArrays;
            }

            var items = ParseResults<T>(loadResult.Results)
                .ToArray();

            if (items.Length > ids.Length)
            {
                throw new InvalidOperationException(String.Format("A load was attempted with transformer {0}, and more than one item was returned per entity - please use {1}[] as the projection type instead of {1}",
                    transformer,
                    typeof(T).Name));
            }

            return items;
        }

        private IEnumerable<T> ParseResults<T>(List<RavenJObject> results)
        {
            foreach (var result in results)
            {
                if (result == null)
                {
                    yield return default(T);
                    continue;
                }

                EnsureNotReadVetoed(result);

                var values = result.Value<RavenJArray>("$values").ToArray();
                foreach (var value in values)
                {
                    if (value == null)
                    {
                        yield return default(T);
                        continue;
                    }

                    if (value.Type != JTokenType.Object)
                    {
                        yield return value.JsonDeserialization<T>();
                        continue;
                    }

                    yield return (T)documentSession.ProjectionToInstance((RavenJObject)value, typeof(T));
                }
            }
        }

        private bool EnsureNotReadVetoed(RavenJObject result)
        {
            var metadata = result.Value<RavenJObject>(Constants.Metadata.Key);
            if (metadata != null)
                documentSession.EnsureNotReadVetoed(metadata); // this will throw on read veto

            return true;
        }
    }
}
