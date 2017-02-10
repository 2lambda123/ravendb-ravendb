using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Raven.Client.Indexes;
using Raven.Client.Indexing;
using Xunit;

namespace SlowTests.MailingList
{
    public class EnumInIndexDef : RavenNewTestBase
    {
        [Fact]
        public void QueryById()
        {
            using (var store = GetDocumentStore())
            {
                new SomeDocumentIndex().Execute(store);
            }
        }

        private class SomeDocument
        {
            public string Id { get; set; }
            public string Text { get; set; }
        }

        private enum SomeEnum
        {
            Value1 = 1,
            Value2 = 2
        }

        private class SomeDocumentIndex : AbstractIndexCreationTask<SomeDocument, SomeDocumentIndex.IndexResult>
        {
            public class IndexResult
            {
                public string Id { get; set; }
                public SomeEnum SomeEnum { get; set; }
            }

            public SomeDocumentIndex()
            {
                Map = docs => from doc in docs
                              select new { Id = doc.Id, SomeEnum = SomeEnum.Value1 };

                Store(x => x.SomeEnum, FieldStorage.Yes);
            }
        }
    }
}
