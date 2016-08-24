using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Raven.Client;
using Raven.Client.Indexing;
using Xunit;

namespace SlowTests.MailingList
{
    public class AutoIndexMerging : RavenTestBase
    {
        private const string SampleLogfileStoreId = "123";

        [Fact]
        public async Task AutoIndexReuseFails()
        {
            using (var store = await GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    // each of these queries will generate an auto index, that will expand on the previous auto index and include additional fields

                    // Auto/Logfiles/ByUploadDateSortByUploadDate
                    string index1Name;
                    FirstQuery(session, out index1Name);

                    // Auto/Logfiles/BySavedAnalysesAndStoreIdAndUploadDateSortByUploadDate
                    string index2Name;
                    SecondQuery(session, out index2Name);

                    //  Auto/Logfiles/BySavedAnalysesAndSharedOnFacebookActionIdAndStoreIdAndUploadDateSortByUploadDate
                    string index3Name;
                    ThirdQuery(session, out index3Name);

                    Assert.Equal(3, GetAutoIndexes(store).Length);

                    // now lets delete the second index
                    store.DatabaseCommands.DeleteIndex(index1Name);
                    store.DatabaseCommands.DeleteIndex(index2Name);

                    FirstQuery(session, out index1Name);
                    SecondQuery(session, out index2Name);
                    ThirdQuery(session, out index3Name);

                    // Auto/Logfiles/BySavedAnalysesAndSharedOnFacebookActionIdAndStoreIdAndUploadDateSortByUploadDate is able to fulfill all requests
                    Assert.Equal(1, GetAutoIndexes(store).Length);
                }
            }
        }

        private static IndexDefinition[] GetAutoIndexes(IDocumentStore store)
        {
            return store.DatabaseCommands.GetIndexes(0, 1024).Where(x => x.Name.StartsWith("Auto/")).ToArray();
        }

        private static int ThirdQuery(IDocumentSession session, out string indexName)
        {
            RavenQueryStatistics stats;
            var results = session
                .Query<Logfile>()
                .Statistics(out stats)
                .Where(x => x.StoreId != SampleLogfileStoreId && x.SharedOnFacebookActionId != null)
                .Count();

            indexName = stats.IndexName;
            return results;
        }

        private static int SecondQuery(IDocumentSession session, out string indexName)
        {
            RavenQueryStatistics stats;
            var results = session
                .Query<Logfile>()
                .Statistics(out stats)
                .Where(x => x.StoreId != SampleLogfileStoreId && x.SavedAnalyses.Any())
                .Count();

            indexName = stats.IndexName;
            return results;
        }

        private static IList<string> FirstQuery(IDocumentSession session, out string indexName)
        {
            var now = DateTime.UtcNow;

            RavenQueryStatistics stats;
            var results = session.Query<Logfile>()
                .Statistics(out stats)
                .Where(x => x.UploadDate >= now.AddMonths(-1))
                .Select(x => x.Owner)
                .Distinct()
                .Take(1024) // see 
                .ToList();

            indexName = stats.IndexName;
            return results;
        }

        private class Logfile
        {
            public DateTime UploadDate { get; set; }
            public string Owner { get; set; }
            public string StoreId { get; set; }
            public string[] SavedAnalyses { get; set; }
            public string SharedOnFacebookActionId { get; set; }
        }
    }
}
