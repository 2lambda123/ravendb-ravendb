//-----------------------------------------------------------------------
// <copyright file="Suggestions.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using FastTests;
using Raven.Client;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.Documents.Queries.Suggestion;
using Xunit;

namespace SlowTests.Tests.Suggestions
{
    public class Suggestions : RavenTestBase
    {
        private class User
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
        }

        public void Setup(IDocumentStore store)
        {
            store.Admin.Send(new PutIndexesOperation(new[] { new IndexDefinition
            {
                Name = "test",
                Maps = { "from doc in docs.Users select new { doc.Name }" },
                Fields = new Dictionary<string, IndexFieldOptions>
                {
                    {
                        "Name",
                        new IndexFieldOptions { Suggestions = true }
                    }
                }
            }}));

            using (var s = store.OpenSession())
            {
                s.Store(new User { Name = "Ayende" });
                s.Store(new User { Name = "Oren" });
                s.SaveChanges();

                s.Query<User>("Test").Customize(x => x.WaitForNonStaleResults()).ToList();
            }
        }

        [Fact(Skip = "Missing feature: Suggestions")]
        public void ExactMatch()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    var suggestionQueryResult = session.Query<User>("Test")
                        .Where(x => x.Name == "Oren")
                        .Suggest(new SuggestionQuery
                        {
                            MaxSuggestions = 10
                        });

                    Assert.Equal(0, suggestionQueryResult.Suggestions.Length);
                }
            }
        }

        [Fact(Skip = "Missing feature: Suggestions")]
        public void UsingLinq()
        {
            using (var store = GetDocumentStore())
            {
                using (var s = store.OpenSession())
                {
                    var suggestionQueryResult = s.Query<User>("test")
                        .Where(x => x.Name == "Owen")
                        .Suggest();

                    Assert.Equal(1, suggestionQueryResult.Suggestions.Length);
                    Assert.Equal("oren", suggestionQueryResult.Suggestions[0]);
                }
            }
        }

        [Fact(Skip = "Missing feature: Suggestions")]
        public void UsingLinq_with_typo_with_options_multiple_fields()
        {
            using (var store = GetDocumentStore())
            {
                using (var s = store.OpenSession())
                {
                    var suggestionQueryResult = s.Query<User>("test")
                        .Where(x => x.Name == "Orin")
                        .Where(x => x.Email == "whatever")
                        .Suggest(new SuggestionQuery { Field = "Name", Term = "Orin" });

                    Assert.Equal(1, suggestionQueryResult.Suggestions.Length);
                    Assert.Equal("oren", suggestionQueryResult.Suggestions[0]);
                }
            }
        }

        [Fact(Skip = "Missing feature: Suggestions")]
        public void UsingLinq_with_typo_multiple_fields_in_reverse_order()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    var suggestionQueryResult = session.Query<User>("test")
                        .Where(x => x.Email == "whatever")
                        .Where(x => x.Name == "Orin")
                        .Suggest(new SuggestionQuery { Field = "Name", Term = "Orin" });

                    Assert.Equal(1, suggestionQueryResult.Suggestions.Length);
                    Assert.Equal("oren", suggestionQueryResult.Suggestions[0]);
                }
            }
        }

        [Fact(Skip = "Missing feature: Suggestions")]
        public void UsingLinq_WithOptions()
        {
            using (var store = GetDocumentStore())
            {
                using (var s = store.OpenSession())
                {
                    var suggestionQueryResult = s.Query<User>("test")
                        .Where(x => x.Name == "Orin")
                        .Suggest(new SuggestionQuery { Accuracy = 0.4f });

                    Assert.Equal(1, suggestionQueryResult.Suggestions.Length);
                    Assert.Equal("oren", suggestionQueryResult.Suggestions[0]);
                }
            }
        }

        [Fact(Skip = "Missing feature: Suggestions")]
        public void WithTypo()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    var suggestionQueryResult = session.Query<User>("Test")
                        .Where(x => x.Name == "Oern") // intentional typo
                        .Suggest(new SuggestionQuery
                        {
                            MaxSuggestions = 10,
                            Accuracy = 0.2f,
                            Distance = StringDistanceTypes.Levenshtein
                        });

                    Assert.Equal(1, suggestionQueryResult.Suggestions.Length);
                    Assert.Equal("oren", suggestionQueryResult.Suggestions[0]);
                }
            }
        }
    }
}
