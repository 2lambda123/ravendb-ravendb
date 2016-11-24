using System;
using System.Collections.Generic;
using System.Linq;
using Raven.NewClient.Abstractions.Data;
using Raven.NewClient.Client.Commands;
using Raven.NewClient.Client.Data.Queries;


namespace Raven.NewClient.Client.Document
{
    public partial class DocumentSession
    {
        public IEnumerator<StreamResult> Stream<T>(IQueryable<T> query)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<StreamResult> Stream<T>(IQueryable<T> query,
            out QueryHeaderInformation queryHeaderInformation)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<StreamResult> Stream<T>(IDocumentQuery<T> query)
        {
            throw new NotImplementedException();
        }

        IEnumerator<StreamResult> ISyncAdvancedSessionOperation.Stream<T>(IDocumentQuery<T> query,
            out QueryHeaderInformation queryHeaderInformation)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<StreamResult> Stream<T>(IDocumentQuery<T> query,
            out QueryHeaderInformation queryHeaderInformation)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<StreamResult> Stream<T>(long? fromEtag, int start = 0, int pageSize = Int32.MaxValue,
            RavenPagingInformation pagingInformation = null, string transformer = null,
            Dictionary<string, object> transformerParameters = null)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<StreamResult> Stream<T>(string startsWith, string matches = null, int start = 0,
            int pageSize = Int32.MaxValue,
            RavenPagingInformation pagingInformation = null, string skipAfter = null, string transformer = null,
            Dictionary<string, object> transformerParameters = null)
        {
            throw new NotImplementedException();
        }
    }
}