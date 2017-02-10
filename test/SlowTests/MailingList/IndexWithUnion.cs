using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Raven.NewClient.Client.Indexes;
using Xunit;

namespace SlowTests.MailingList
{
    public class IndexWithUnion : RavenNewTestBase
    {
        private class Sermon
        {
#pragma warning disable 649
            public string Description, Series, Speaker, Title;
            public string[] Tags;
#pragma warning restore 649
        }

        private class Index : AbstractMultiMapIndexCreationTask
        {
            public Index()
            {
                AddMap<Sermon>(items => from x in items
                                        select new
                                        {
                                            Content = new string[]
                                            {
                                                x.Description, x.Series, x.Speaker,
                                                x.Title
                                            }.Union(x.Tags)
                                        });
            }
        }

        [Fact]
        public void CanCreateIndex()
        {
            using (var x = GetDocumentStore())
            {
                new Index().Execute(x);
            }
        }
    }
}
