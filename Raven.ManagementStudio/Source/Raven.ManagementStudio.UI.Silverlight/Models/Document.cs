﻿namespace Raven.ManagementStudio.UI.Silverlight.Models
{
    using System;
    using System.Collections.Generic;
    using Client;
    using Newtonsoft.Json.Linq;
    using Raven.Database;
    using Newtonsoft.Json;

    public class Document
    {
        private readonly IDictionary<string, JToken> data;
        private readonly IDictionary<string, JToken> metadata;
        private readonly JsonDocument jsonDocument;

        public Document(JsonDocument jsonDocument)
        {
            data = new Dictionary<string, JToken>();
            metadata = new Dictionary<string, JToken>();
            
            this.jsonDocument = jsonDocument;
            Id = jsonDocument.Key;
            data = ParseJsonToDictionary(jsonDocument.DataAsJson);
            metadata = ParseJsonToDictionary(jsonDocument.Metadata);
        }

        public string Id { get; private set; }

        public IDictionary<string, JToken> Data
        {
            get { return data; }
        }

        public IDictionary<string, JToken> Metadata
        {
            get { return metadata; }
        }

        public static string ParseExceptionMessage { get; set; }

        public JsonDocument JsonDocument
        {
            get { return jsonDocument; }
        }

        public void SetData(string json)
        {
            jsonDocument.DataAsJson = JObject.Parse(json);     
        }

        public void SetMetadata(string json)
        {
            jsonDocument.Metadata = JObject.Parse(json);
        }

        public void SetId(string id)
        {
            jsonDocument.Key = id;
        }

        public void Save(IAsyncDocumentSession session, Action<object> callback)
        {
            session.Store(jsonDocument);
            session.SaveChangesAsync();
            Id = jsonDocument.Key;
        }

        public static bool ValidateJson(string json)
        {
            try
            {
                JObject.Parse(json);
                ParseExceptionMessage = string.Empty;
                return true;
            }
            catch (JsonReaderException exception)
            {
                ParseExceptionMessage = exception.Message;
                return false;
            }
        }

        private static IDictionary<string, JToken> ParseJsonToDictionary(JObject dataAsJson)
        {
            IDictionary<string, JToken> result = new Dictionary<string, JToken>();

            foreach (var d in dataAsJson)
            {
                result.Add(d.Key, d.Value);
            }

            return result;
        }
    }
}
