using System.Threading.Tasks;
using NewClientTests;
using Raven.NewClient.Client.Exceptions;

using Xunit;

using User = SlowTests.Core.Utils.Entities.User;

namespace NewClientTests.NewClient.Raven.Tests.Core.Sessiont
{
    public class OptimisticConcurrency : RavenTestBase
    {
#if DNXCORE50
        public OptimisticConcurrency(TestServerFixture fixture)
            : base(fixture)
        {

        }
#endif
        [Fact]
        public void CanUseOptmisticConcurrency()
        {
            const string entityId = "users/1";

            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    Assert.False(session.Advanced.UseOptimisticConcurrency);
                    session.Advanced.UseOptimisticConcurrency = true;

                    session.Store(new User { Id = entityId, Name = "User1" });
                    session.SaveChanges();

                    using (var otherSession = store.OpenSession())
                    {
                        var otherUser = otherSession.Load<User>(entityId);
                        otherUser.Name = "OtherName";
                        otherSession.Store(otherUser);
                        otherSession.SaveChanges();
                    }

                    var user = session.Load<User>("users/1");
                    user.Name = "Name";
                    session.Store(user);
                    //TODO
                    //var e = Assert.Throws<ConcurrencyException>(() => session.SaveChanges());
                }
            }
        }

        [Fact]
        public void CanBypassOptmisticConcurrencyCheckByExplicitlyProvidingAnEtagOfNull()
        {
            const string entityId = "users/1";

            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Advanced.UseOptimisticConcurrency = true;

                    session.Store(new User { Id = entityId, Name = "User1" });
                    session.SaveChanges();

                    using (var otherSession = store.OpenSession())
                    {
                        var otherUser = otherSession.Load<User>(entityId);
                        otherUser.Name = "OtherName";
                        otherSession.Store(otherUser);
                        otherSession.SaveChanges();
                    }

                    var user = session.Load<User>("users/1");
                    user.Name = "Name";
                    session.Store(user, etag: null);
                    session.SaveChanges();
                }
            }
        }

        [Fact]
        public void CanBypassOptmisticConcurrencyCheckByExplicitlyProvidingAnEtagOfNullToStore()
        {
            const string entityId = "users/1";

            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Advanced.UseOptimisticConcurrency = true;

                    session.Store(new User { Id = entityId, Name = "User1" });
                    session.SaveChanges();

                    using (var otherSession = store.OpenSession())
                    {
                        var otherUser = otherSession.Load<User>(entityId);
                        otherUser.Name = "OtherName";
                        otherSession.Store(otherUser);
                        otherSession.SaveChanges();
                    }

                    var user = session.Load<User>("users/1");
                    user.Name = "Name";
                    session.Store(user, etag: null);
                    session.SaveChanges();
                }
            }
        }

        [Fact]
        public void CanBypassOptmisticConcurrencyCheckByExplicitlyProvidingAnEtagAndAnIdOfNullToStore()
        {
            const string entityId = "users/1";

            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Advanced.UseOptimisticConcurrency = true;

                    session.Store(new User { Id = entityId, Name = "User1" });
                    session.SaveChanges();

                    using (var otherSession = store.OpenSession())
                    {
                        var otherUser = otherSession.Load<User>(entityId);
                        otherUser.Name = "OtherName";
                        otherSession.Store(otherUser);
                        otherSession.SaveChanges();
                    }

                    var user = session.Load<User>("users/1");
                    user.Name = "Name";
                    session.Store(user, etag: null);
                    session.SaveChanges();
                }
            }
        }

        [Fact]
        public async Task CanBypassOptmisticConcurrencyCheckByExplicitlyProvidingAnEtagOfNullToStoreAsync()
        {
            const string entityId = "users/1";

            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenAsyncSession())
                {
                    session.Advanced.UseOptimisticConcurrency = true;

                    await session.StoreAsync(new User { Id = entityId, Name = "User1" });
                    await session.SaveChangesAsync();

                    using (var otherSession = store.OpenSession())
                    {
                        var otherUser = otherSession.Load<User>(entityId);
                        otherUser.Name = "OtherName";
                        otherSession.Store(otherUser);
                        otherSession.SaveChanges();
                    }

                    var user = await session.LoadAsync<User>("users/1");
                    user.Name = "Name";
                    await session.StoreAsync(user, null, entityId);
                    await session.SaveChangesAsync();
                }
            }
        }

        [Fact]
        public async Task CanBypassOptmisticConcurrencyCheckByExplicitlyProvidingAnEtagAndAnIdOfNullToStoreAsync()
        {
            const string entityId = "users/1";

            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenAsyncSession())
                {
                    session.Advanced.UseOptimisticConcurrency = true;

                    await session.StoreAsync(new User { Id = entityId, Name = "User1" });
                    await session.SaveChangesAsync();

                    using (var otherSession = store.OpenSession())
                    {
                        var otherUser = otherSession.Load<User>(entityId);
                        otherUser.Name = "OtherName";
                        otherSession.Store(otherUser);
                        otherSession.SaveChanges();
                    }

                    var user = await session.LoadAsync<User>("users/1");
                    user.Name = "Name";
                    await session.StoreAsync(user, null, entityId);
                    await session.SaveChangesAsync();
                }
            }
        }
    }
}
