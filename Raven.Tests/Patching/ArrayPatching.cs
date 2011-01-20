//-----------------------------------------------------------------------
// <copyright file="ArrayPatching.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Raven.Database.Exceptions;
using Raven.Database.Json;
using Raven.Http.Exceptions;
using Xunit;

namespace Raven.Tests.Patching
{
    public class ArrayPatching
    {
        private readonly JObject doc = JObject.Parse(@"{ title: ""A Blog Post"", body: ""html markup"", comments: [{""author"":""ayende"",""text"":""good post 1""},{author: ""ayende"", text:""good post 2""}] }");

        [Fact]
        public void AddingItemToArray()
        {
        	var patchedDoc = new JsonPatcher(doc).Apply(
        		new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Add,
        				Name = "comments",
        				Value = JObject.Parse(@"{""author"":""oren"",""text"":""agreed""}")

        			}
        		});

            Assert.Equal(@"{""title"":""A Blog Post"",""body"":""html markup"",""comments"":[{""author"":""ayende"",""text"":""good post 1""},{""author"":""ayende"",""text"":""good post 2""},{""author"":""oren"",""text"":""agreed""}]}", 
                patchedDoc.ToString(Formatting.None));
        }

        [Fact]
        public void AddingItemToArray_WithConcurrency_Ok()
        {
            var patchedDoc = new JsonPatcher(doc).Apply(
				new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Add,
        				Name = "comments",
						PrevVal = JArray.Parse(@"[{""author"":""ayende"",""text"":""good post 1""},{author: ""ayende"", text:""good post 2""}]"),
        				Value = JObject.Parse(@"{""author"":""oren"",""text"":""agreed""}")

        			}
        		});

            Assert.Equal(@"{""title"":""A Blog Post"",""body"":""html markup"",""comments"":[{""author"":""ayende"",""text"":""good post 1""},{""author"":""ayende"",""text"":""good post 2""},{""author"":""oren"",""text"":""agreed""}]}",
                patchedDoc.ToString(Formatting.None));
        }

        [Fact]
        public void AddingItemToArray_WithConcurrency_Error()
        {
        	Assert.Throws<ConcurrencyException>(() => new JsonPatcher(doc).Apply(
        		new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Add,
        				Name = "comments",
        				PrevVal =
        					JArray.Parse(
        						@"[{""author"":""ayende"",""text"":""good post 1""},{author: ""ayende"", text:""good post 1""}]"),
        				Value = JObject.Parse(@"{""author"":""oren"",""text"":""agreed""}")

        			}
        		}));
        }


        [Fact]
        public void AddingItemToArrayWhenArrayDoesNotExists()
        {
            var patchedDoc = new JsonPatcher(doc).Apply(
				new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Add,
        				Name = "blog_id",
        				Value = new JValue(1),
        			}
        		});

            Assert.Equal(@"{""title"":""A Blog Post"",""body"":""html markup"",""comments"":[{""author"":""ayende"",""text"":""good post 1""},{""author"":""ayende"",""text"":""good post 2""}],""blog_id"":[1]}",
                patchedDoc.ToString(Formatting.None));
        }

        [Fact]
        public void AddingItemToArrayWhenArrayDoesNotExists_WithConcurrency_Ok()
        {
            var patchedDoc = new JsonPatcher(doc).Apply(
				new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Add,
        				Name = "blog_id",
        				Value = new JValue(1),
						PrevVal = JObject.Parse("{'a': undefined}").Property("a").Value
        			}
        		});

            Assert.Equal(@"{""title"":""A Blog Post"",""body"":""html markup"",""comments"":[{""author"":""ayende"",""text"":""good post 1""},{""author"":""ayende"",""text"":""good post 2""}],""blog_id"":[1]}",
                patchedDoc.ToString(Formatting.None));
        }


        [Fact]
        public void AddingItemToArrayWhenArrayDoesNotExists_WithConcurrency_Error()
        {
        	Assert.Throws<ConcurrencyException>(() => new JsonPatcher(doc).Apply(
        		new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Add,
        				Name = "blog_id",
        				Value = new JValue(1),
        				PrevVal = new JArray()
        			},
        		}));
        }

        [Fact]
        public void CanAddServeralItemsToSeveralDifferentPartsAtTheSameTime()
        {
            var patchedDoc = new JsonPatcher(doc).Apply(
				new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Add,
        				Name = "blog_id",
        				Value = new JValue(1)
        			},
					new PatchRequest
        			{
        				Type = PatchCommandType.Add,
        				Name = "blog_id",
        				Value = new JValue(2)
        			},
					new PatchRequest
        			{
        				Type = PatchCommandType.Set,
        				Name = "title",
        				Value = new JValue("abc")
        			},
        		});

            Assert.Equal(@"{""title"":""abc"",""body"":""html markup"",""comments"":[{""author"":""ayende"",""text"":""good post 1""},{""author"":""ayende"",""text"":""good post 2""}],""blog_id"":[1,2]}",
                patchedDoc.ToString(Formatting.None));
        }

        [Fact]
        public void RemoveItemFromArray()
        {
            var patchedDoc = new JsonPatcher(doc).Apply(
                new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Remove,
        				Name = "comments",
						Position = 0
        			},
        		});

            Assert.Equal(@"{""title"":""A Blog Post"",""body"":""html markup"",""comments"":[{""author"":""ayende"",""text"":""good post 2""}]}",
                patchedDoc.ToString(Formatting.None));
        }
        
        [Fact]
        public void RemoveItemFromArrayByValue()
        {
            var patchedDoc = new JsonPatcher(JObject.Parse(@"{ name: ""Joe Doe"", roles: [""first/role"", ""second/role"", ""third/role""] }")).Apply(
                new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Remove,
        				Name = "roles",
						Value = "second/role"
        			},
        		});

            Assert.Equal(@"{""name"":""Joe Doe"",""roles"":[""first/role"",""third/role""]}",
                         patchedDoc.ToString(Formatting.None));

        }
        
        [Fact]
        public void RemoveItemFromArrayByNonExistingValue()
        {
            var value = @"{""name"":""Joe Doe"",""roles"":[""first/role"",""second/role"",""third/role""]}";
            var patchedDoc = new JsonPatcher(JObject.Parse(value));

            var result = patchedDoc.Apply(
                new[]
                {
                    new PatchRequest
                    {
                        Type = PatchCommandType.Remove,
                        Name = "roles",
                        Value = "this/does/not/exist"
                    },
                });

            Assert.Equal(value, result.ToString(Formatting.None));
        }

        [Fact]
        public void RemoveItemFromArray_WithConcurrency_Ok()
        {
            var patchedDoc = new JsonPatcher(doc).Apply(
                new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Remove,
        				Name = "comments",
						Position = 0,
						PrevVal = JArray.Parse(@"[{""author"":""ayende"",""text"":""good post 1""},{author: ""ayende"", text:""good post 2""}]")
        			},
        		});

            Assert.Equal(@"{""title"":""A Blog Post"",""body"":""html markup"",""comments"":[{""author"":""ayende"",""text"":""good post 2""}]}",
                patchedDoc.ToString(Formatting.None));
        }


        [Fact]
        public void RemoveItemFromArray_WithConcurrency_Error()
        {
            Assert.Throws<ConcurrencyException>(() => new JsonPatcher(doc).Apply(
                 new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Remove,
        				Name = "comments",
						Position = 0,
						PrevVal = JArray.Parse(@"[{""author"":""ayende"",""text"":""diffrent value""},{author: ""ayende"", text:""good post 2""}]")
        			},
        		}));
        }

        [Fact]
        public void InsertItemToArray()
        {
            var patchedDoc = new JsonPatcher(doc).Apply(
				new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Insert,
        				Name = "comments",
						Position = 1,
						Value = JObject.Parse(@"{""author"":""ayende"",""text"":""good post 1.5""}")
        			},
        		});

            Assert.Equal(@"{""title"":""A Blog Post"",""body"":""html markup"",""comments"":[{""author"":""ayende"",""text"":""good post 1""},{""author"":""ayende"",""text"":""good post 1.5""},{""author"":""ayende"",""text"":""good post 2""}]}",
                patchedDoc.ToString(Formatting.None));
        }

        [Fact]
        public void InsertItemToArray_WithConcurrency_Ok()
        {
            var patchedDoc = new JsonPatcher(doc).Apply(
				new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Insert,
        				Name = "comments",
						Position = 1,
						Value = JObject.Parse(@"{""author"":""ayende"",""text"":""good post 1.5""}"),
						PrevVal = JArray.Parse(@"[{""author"":""ayende"",""text"":""good post 1""},{author: ""ayende"", text:""good post 2""}]")
        			},
        		});

            Assert.Equal(@"{""title"":""A Blog Post"",""body"":""html markup"",""comments"":[{""author"":""ayende"",""text"":""good post 1""},{""author"":""ayende"",""text"":""good post 1.5""},{""author"":""ayende"",""text"":""good post 2""}]}",
                patchedDoc.ToString(Formatting.None));
        }

        [Fact]
        public void InsertItemToArray_WithConcurrency_Error()
        {
            Assert.Throws<ConcurrencyException>(() => new JsonPatcher(doc).Apply(
				new[]
        		{
        			new PatchRequest
        			{
        				Type = PatchCommandType.Insert,
        				Name = "comments",
						Position = 1,
						Value = JObject.Parse(@"{""author"":""yet another author"",""text"":""good post 1.5""}"),
						PrevVal = JArray.Parse(@"[{""author"":""different"",""text"":""good post 1""},{author: ""ayende"", text:""good post 2""}]")
        			},
        		}));
        }
    }
}
