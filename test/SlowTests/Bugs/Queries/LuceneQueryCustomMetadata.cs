using System.Dynamic;
using FastTests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SlowTests.Bugs.Queries
{
    public class LuceneQueryCustomMetadata : RavenTestBase
    {
        private const string PropertyName = "MyCustomProperty";

        [Fact]
        public void SuccessTest1()
        {
            using (var documentStore = GetDocumentStore())
            {
                dynamic expando = new ExpandoObject();

                using (var session = documentStore.OpenSession())
                {
                    session.Store(expando);

                    var metadata = session.Advanced.GetMetadataFor((ExpandoObject)expando);

                    metadata[PropertyName] = JToken.FromObject(true).ToString();

                    session.SaveChanges();
                }

                using (var session = documentStore.OpenSession())
                {
                    var loaded = session.Load<ExpandoObject>((string)expando.Id);

                    var metadata = session.Advanced.GetMetadataFor(loaded);

                    string token;

                    Assert.True(metadata.TryGetValue(PropertyName , out token));
                    Assert.NotNull(token);

                }
            }
        }

        [Fact]
        public void SuccessTest2()
        {
            using (var documentStore = GetDocumentStore())
            {
                dynamic expando = new ExpandoObject();

                using (var session = documentStore.OpenSession())
                {
                    session.Store(expando);

                    var metadata = session.Advanced.GetMetadataFor((ExpandoObject)expando);

                    metadata[PropertyName] = "true";

                    session.SaveChanges();
                }

                using (var session = documentStore.OpenSession())
                {
                    dynamic loaded = session.Advanced.DocumentQuery<dynamic>()
                        .WhereEquals("@metadata.@collection",
                                     documentStore.Conventions.GetCollectionName(typeof(ExpandoObject)))
                        .FirstOrDefault();

                    Assert.NotNull(loaded);
                }
            }
        }

        [Fact]
        public void FailureTest()
        {
            using (var documentStore = GetDocumentStore())
            {
                dynamic expando = new ExpandoObject();

                using (var session = documentStore.OpenSession())
                {
                    session.Store(expando);

                    var metadata = session.Advanced.GetMetadataFor((ExpandoObject)expando);

                    metadata[PropertyName] = "true";

                    session.SaveChanges();
                }

                using (var session = documentStore.OpenSession())
                {
                    dynamic loaded =
                        session.Advanced.DocumentQuery<dynamic>().WhereEquals("@metadata." + PropertyName, true)
                            .FirstOrDefault();

                    Assert.NotNull(loaded);
                }
            }
        }
    }
}
