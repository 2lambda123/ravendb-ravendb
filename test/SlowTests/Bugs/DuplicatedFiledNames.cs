using FastTests;
using Xunit;
using System.Linq;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;

namespace SlowTests.Bugs
{
    public class DuplicatedFiledNames : RavenTestBase
    {
        [Fact]
        public void ShouldNotDoThat()
        {
            using(var store = GetDocumentStore())
            {
                store.Admin.Send(new PutIndexesOperation(new[] { new IndexDefinition
                {
                    Maps = { "from doc in docs.ClickBalances select new { doc.AccountId } " },
                    Name = "test"
                }}));

                using(var s = store.OpenSession())
                {
                    var accountId = 1;
                    s.Query<ClickBalance>("test")
                        .Where(x => x.AccountId == accountId)
                        .ToList();
                }
            }
        }

        private class ClickBalance
        {
            public int AccountId { get; set; }
        }
    }

 
}
