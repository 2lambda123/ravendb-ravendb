using FastTests.Server.Basic.Entities;
using NewClientTests;
using Xunit;

using User = SlowTests.Core.Utils.Entities.User;

namespace NewClientTests.NewClient.Raven.Tests.Core.Session
{ 
    public class Includes : RavenTestBase
    {
#if DNXCORE50
        public Includes(TestServerFixture fixture)
            : base(fixture)
        {

        }
#endif

        [Fact]
        public void BasicInclude()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    var user = new User
                    {
                        Id = "users/1",
                        AddressId = "addresses/1",
                        Name = "John"
                    };

                    var address = new Address
                    {
                        Id = "addresses/1",
                        City = "New York",
                        Country = "USA",
                        Street = "Wall Street"
                    };

                    session.Store(user);
                    session.Store(address);

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    Assert.Equal(0, session.Advanced.NumberOfRequests);

                    var user = session
                        .Include<User>(x => x.AddressId)
                        .Load<User>("users/1");

                    Assert.Equal(1, session.Advanced.NumberOfRequests);

                    Assert.NotNull(user);
                    Assert.Equal("John", user.Name);

                    var address = session.Load<Address>(user.AddressId);

                    Assert.Equal(1, session.Advanced.NumberOfRequests);

                    Assert.Equal("New York", address.City);
                }
            }
        }
    }
}
