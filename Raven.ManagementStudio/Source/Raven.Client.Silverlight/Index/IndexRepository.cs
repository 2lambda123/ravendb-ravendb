﻿namespace Raven.Client.Silverlight.Index
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Browser;
    using System.Threading;
    using Newtonsoft.Json.Linq;
    using Raven.Client.Silverlight.Common;
    using Raven.Client.Silverlight.Common.Mappers;
    using Raven.Client.Silverlight.Data;

    public class IndexRepository : BaseRepository<JsonIndex>, IIndexRepository
    {
        public IndexRepository(Uri databaseAddress)
        {
            WebRequest.RegisterPrefix("http://", WebRequestCreator.ClientHttp);

            this.ContextStorage = new Dictionary<HttpWebRequest, SynchronizationContext>();
            this.DatabaseAddress = databaseAddress;

            this.Mapper = new IndexMapper();
        }

        public void Get<T>(string name, CallbackFunction.Load<T> callback, CallbackFunction.Store<T> storeCallback) where T : JsonIndex
        {
            HttpWebRequest request;
            this.CreateContext(new Uri(string.Format(DatabaseUrl.IndexGet, this.DatabaseAddress, name)), RequestMethod.GET, out request);

            request.BeginGetResponse((result) => this.Get_Callback(name, request, result, callback, storeCallback), request);
        }

        public void GetMany<T>(string[] names, IList<T> existingEntities, CallbackFunction.Load<IList<T>> callback, CallbackFunction.Store<IList<T>> storeCallback) where T : JsonIndex
        {
            HttpWebRequest request;

            if (names == null || names.Length == 0)
            {
                this.CreateContext(new Uri(string.Format(DatabaseUrl.IndexGetAll, this.DatabaseAddress)), RequestMethod.GET, out request);

                request.BeginGetResponse((result) => this.GetMany_Callback(existingEntities, request, result, callback, storeCallback), request);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public void Put<T>(T entity, CallbackFunction.Save<T> callback, CallbackFunction.Store<T> storeCallback) where T : JsonIndex
        {
            HttpWebRequest request;
            this.CreateContext(new Uri(string.Format(DatabaseUrl.IndexPut, this.DatabaseAddress, entity.Name)), RequestMethod.PUT, out request);

            request.BeginGetRequestStream(
                (requestResult) =>
                {
                    var writer = new StreamWriter(request.EndGetRequestStream(requestResult));

                    writer.Write(entity.ToJson());
                    writer.Close();

                    request.BeginGetResponse((responseResult) => this.Save_Callback(entity, request, responseResult, callback, storeCallback), request);
                },
                request);
        }

        public void Delete<T>(T entity, CallbackFunction.Save<T> callback) where T : JsonIndex
        {
            HttpWebRequest request;
            this.CreateContext(new Uri(string.Format(DatabaseUrl.IndexPut, this.DatabaseAddress, entity.Name)), RequestMethod.DELETE, out request);

            request.BeginGetResponse((result) => this.Delete_Callback(entity, request, result, callback), request);
        }

        private void Get_Callback<T>(string name, HttpWebRequest request, IAsyncResult result, CallbackFunction.Load<T> callback, CallbackFunction.Store<T> storeCallback) where T : JsonIndex
        {
            var context = this.GetContext(request);
            this.DeleteContext(request);

            HttpStatusCode statusCode;
            Exception exception;
            var json = this.GetResponseStream(request, result, out statusCode, out exception);

            var document = this.Mapper.Map(json); // TODO
            document.Id = document.Name = name;

            var loadResponse = new LoadResponse<T>()
            {
                Data = document as T,
                StatusCode = statusCode,
                Exception = exception
            };

            context.Post(
                delegate
                {
                    callback.Invoke(loadResponse);
                },
                null);

            if (loadResponse.IsSuccess)
            {
                context.Post(
                    delegate
                    {
                        storeCallback.Invoke(loadResponse.Data);
                    },
                    null);
            }
        }

        private void GetMany_Callback<T>(IList<T> existingEntities, HttpWebRequest request, IAsyncResult result, CallbackFunction.Load<IList<T>> callback, CallbackFunction.Store<IList<T>> storeCallback) where T : JsonIndex
        {
            var context = this.GetContext(request);
            this.DeleteContext(request);

            HttpStatusCode statusCode;
            Exception exception;
            var json = this.GetResponseStream(request, result, out statusCode, out exception);
            var array = JArray.Parse(json);

            var entities = array.Select(jsonIndex => (T)this.Mapper.Map(jsonIndex.ToString()));
            var responseResult = existingEntities != null ? entities.Concat(existingEntities).ToList() : entities.ToList();

            var loadResponse = new LoadResponse<IList<T>>()
                                   {
                                       Data = responseResult,
                                       StatusCode = statusCode,
                                       Exception = exception
                                   };

            context.Post(
                delegate
                {
                    callback.Invoke(loadResponse);
                },
                null);

            if (loadResponse.IsSuccess)
            {
                context.Post(
                    delegate
                    {
                        storeCallback.Invoke(loadResponse.Data);
                    },
                    null);
            }
        }

        private void Save_Callback<T>(T entity, HttpWebRequest request, IAsyncResult result, CallbackFunction.Save<T> callback, CallbackFunction.Store<T> storeCallback) where T : JsonIndex
        {
            var context = this.GetContext(request);
            this.DeleteContext(request);

            HttpStatusCode statusCode;
            Exception exception;
            var json = this.GetResponseStream(request, result, out statusCode, out exception);

            var responseJson = JObject.Parse(json);

            entity.Name = entity.Id = responseJson["Index"].ToString().Replace("\"", string.Empty);

            var saveResponse = new SaveResponse<T>()
                                   {
                                       Data = entity,
                                       StatusCode = statusCode,
                                       Exception = exception
                                   };

            context.Post(
                delegate
                {
                    callback.Invoke(saveResponse);
                },
                null);

            if (saveResponse.IsSuccess)
            {
                context.Post(
                    delegate
                    {
                        storeCallback.Invoke(saveResponse.Data);
                    },
                    null);
            }
        }

        private void Delete_Callback<T>(T entity, HttpWebRequest request, IAsyncResult result, CallbackFunction.Save<T> callback) where T : JsonIndex
        {
            var context = this.GetContext(request);
            this.DeleteContext(request);

            HttpStatusCode statusCode;
            Exception exception;
            var json = this.GetResponseStream(request, result, out statusCode, out exception);

            var deleteResponse = new DeleteResponse<T>()
                                     {
                                         Data = entity,
                                         StatusCode = statusCode,
                                         Exception = exception
                                     };

            context.Post(
                delegate
                {
                    callback.Invoke(deleteResponse);
                },
                null);
        }
    }
}
