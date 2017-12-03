﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Smuggler;
using Raven.Tests.Core.Utils.Entities;
using Sparrow.Json;
using Xunit;

namespace FastTests.Client
{
    public class UniqueValues : RavenTestBase
    {
        [Fact]
        public async Task CanPutUniqueString()
        {
            DoNotReuseServer();
            var store = GetDocumentStore();
            await store.Operations.SendAsync(new PutCompareExchangeValueOperation<string>("test", "Karmel", 0));
            var res = await store.Operations.SendAsync(new GetCompareExchangeValueOperation<string>("test"));
            Assert.Equal("Karmel", res.Value);
            Assert.True(res.Successful);
        }

        [Fact]
        public async Task CanPutUniqueObject()
        {
            DoNotReuseServer();
            var store = GetDocumentStore();
            var res = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", new User
            {
                Name = "Karmel"
            }, 0));
            Assert.True(res.Successful);
            Assert.Equal("Karmel", res.Value.Name);
        }

        [Fact]
        public async Task CanPutMultiDifferentValues()
        {
            DoNotReuseServer();
            var store = GetDocumentStore();
            var res = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", new User
            {
                Name = "Karmel"
            }, 0));
            var res2 = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test2", new User
            {
                Name = "Karmel"
            }, 0));

            Assert.Equal("Karmel", res.Value.Name);
            Assert.True(res.Successful);
            Assert.Equal("Karmel", res2.Value.Name);
            Assert.True(res2.Successful);
        }

        [Fact]
        public async Task CanExportAndImportCmpXchg()
        {
            var file = Path.GetTempFileName();
            DoNotReuseServer();
            var store = GetDocumentStore();
            var store2 = GetDocumentStore();
            var res = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", new User
            {
                Name = "Karmel"
            }, 0));
            var res2 = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test2", new User
            {
                Name = "Karmel"
            }, 0));

            Assert.Equal("Karmel", res.Value.Name);
            Assert.True(res.Successful);
            Assert.Equal("Karmel", res2.Value.Name);
            Assert.True(res2.Successful);

            WaitForUserToContinueTheTest(store);

            await store.Smuggler.ExportAsync(new DatabaseSmugglerExportOptions(), file);
            await store2.Smuggler.ImportAsync(new DatabaseSmugglerImportOptions(), file);

            res = await store2.Operations.SendAsync(new GetCompareExchangeValueOperation<User>("test"));
            Assert.Equal("Karmel", res.Value.Name);
            Assert.True(res.Successful);

            res = await store2.Operations.SendAsync(new GetCompareExchangeValueOperation<User>("test2"));
            Assert.Equal("Karmel", res.Value.Name);
            Assert.True(res.Successful);
        }

        [Fact]
        public async Task CanListCompareExchange()
        {
            DoNotReuseServer();
            var store = GetDocumentStore();
            var res = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", new User
            {
                Name = "Karmel"
            }, 0));
            var res2 = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test2", new User
            {
                Name = "Karmel"
            }, 0));

            Assert.Equal("Karmel", res.Value.Name);
            Assert.True(res.Successful);
            Assert.Equal("Karmel", res2.Value.Name);
            Assert.True(res2.Successful);
            
            var list = (await store.Operations.SendAsync(new ListCompareExchangeValuesOperation("test"))).ToList();
            Assert.Equal(2, list.Count);
            Assert.Equal("test", list[0].Key);
            Assert.Equal("Karmel", ((User)EntityToBlittable.ConvertToEntity(typeof(User),"test", (BlittableJsonReaderObject)list[0].Value, store.Conventions)).Name);
            Assert.Equal("test2", list[1].Key);
            Assert.Equal("Karmel", ((User)EntityToBlittable.ConvertToEntity(typeof(User),"test", (BlittableJsonReaderObject)list[0].Value, store.Conventions)).Name);
        }
        
        [Fact]
        public async Task CanRemoveUnique()
        {
            DoNotReuseServer();
            var store = GetDocumentStore();
            
            var res = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<string>("test", "Karmel", 0));
            Assert.Equal("Karmel", res.Value);
            Assert.True(res.Successful);
            
            res = await store.Operations.SendAsync(new RemoveCompareExchangeOperation<string>("test", res.Index));
            Assert.True(res.Successful);
        }
        
        [Fact]
        public async Task RemoveUniqueFailed()
        {
            DoNotReuseServer();
            var store = GetDocumentStore();
            
            var res = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<string>("test", "Karmel", 0));
            Assert.Equal("Karmel", res.Value);
            Assert.True(res.Successful);
            
            res = await store.Operations.SendAsync(new RemoveCompareExchangeOperation<string>("test", 0));
            Assert.False(res.Successful);
            
            res = await store.Operations.SendAsync(new GetCompareExchangeValueOperation<string>("test"));
            Assert.Equal("Karmel", res.Value);
            Assert.True(res.Successful);
        }
        
        [Fact]
        public async Task ReturnCurrentValueWhenPuttingConcurrently()
        {
            DoNotReuseServer();
            var store = GetDocumentStore();
            var res = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", new User
            {
                Name = "Karmel"
            }, 0));
            var res2 = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", new User
            {
                Name = "Karmel2"
            }, 0));
            Assert.True(res.Successful);
            Assert.False(res2.Successful);
            Assert.Equal("Karmel", res.Value.Name);
            Assert.Equal("Karmel", res2.Value.Name);

            res2 = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", new User
            {
                Name = "Karmel2"
            }, res2.Index));
            ;
            Assert.True(res2.Successful);
            Assert.Equal("Karmel2", res2.Value.Name);
        }

        [Fact]
        public async Task CanGetIndexValue()
        {
            DoNotReuseServer();
            var store = GetDocumentStore();
            await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", new User
            {
                Name = "Karmel"
            }, 0));
            var res = await store.Operations.SendAsync(new GetCompareExchangeValueOperation<User>("test"));
            Assert.Equal("Karmel", res.Value.Name);
            Assert.True(res.Successful);

            var res2 = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", new User
            {
                Name = "Karmel2"
            }, res.Index));
            Assert.True(res2.Successful);
            Assert.Equal("Karmel2", res2.Value.Name);
        }

        [Fact]
        public async Task SaveSameValuesToDifferentDatabases()
        {
            DoNotReuseServer();
            var store = GetDocumentStore(caller: $"CmpExchangeTest1-{new Guid()}");
            var store2 = GetDocumentStore(caller: $"CmpExchangeTest2-{new Guid()}");
            var user  = new User{Name = "Karmel"};
            var res = await store.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test",user,0));
            var res2 = await store2.Operations.SendAsync(new PutCompareExchangeValueOperation<User>("test", user, 0));
            Assert.Equal("Karmel", res.Value.Name);
            Assert.Equal("Karmel", res2.Value.Name);
            Assert.True(res.Successful);
            Assert.True(res2.Successful);
        }
    }
}
