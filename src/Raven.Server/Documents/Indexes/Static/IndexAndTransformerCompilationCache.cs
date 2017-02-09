﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Raven.Abstractions.Indexing;
using Raven.Client.Indexing;
using Raven.Server.Documents.Transformers;
using Sparrow;

namespace Raven.Server.Documents.Indexes.Static
{
    /// <summary>
    /// This is a static class because creating indexes is expensive, we want to cache them 
    /// as much as possible, even across different databases and database instansiation. Per process,
    /// we are going to have a single cache for all indexes. This also plays nice with testing, which 
    /// will build up and tear down a server frequently, so we can still reduce the cost of compiling 
    /// the indexes.
    /// </summary>
    public static class IndexAndTransformerCompilationCache
    {
        public static readonly ConcurrentDictionary<CacheKey, Lazy<StaticIndexBase>> IndexCache = new ConcurrentDictionary<CacheKey, Lazy<StaticIndexBase>>();

        private static readonly ConcurrentDictionary<CacheKey, Lazy<TransformerBase>> TransformerCache = new ConcurrentDictionary<CacheKey, Lazy<TransformerBase>>();

        public static StaticIndexBase GetIndexInstance(IndexDefinition definition, out string[] collections)
        {
            var list = new List<string>();
            list.AddRange(definition.Maps);
            if (definition.Reduce != null)
                list.Add(definition.Reduce);

            var key = new CacheKey(list)
            {
                OutputReduceToCollection = definition.OutputReduceToCollection,
                IndexName = definition.Name,
            };
            Func<StaticIndexBase> createIndex = () => IndexAndTransformerCompiler.Compile(definition);
            var result = IndexCache.GetOrAdd(key, _ => new Lazy<StaticIndexBase>(createIndex));

            var staticIndexBase = result.Value;
            collections = key.Collections = staticIndexBase.Maps.Keys.ToArray();
            return staticIndexBase;
        }

        public static TransformerBase GetTransformerInstance(TransformerDefinition definition)
        {
            var list = new List<string>
            {
                definition.TransformResults
            };

            var key = new CacheKey(list);
            Func<TransformerBase> createTransformer = () => IndexAndTransformerCompiler.Compile(definition);
            var result = TransformerCache.GetOrAdd(key, _ => new Lazy<TransformerBase>(createTransformer));
            return result.Value;
        }

        public class CacheKey : IEquatable<CacheKey>
        {
            private readonly int _hash;
            private readonly List<string> _items;

            public string OutputReduceToCollection;
            public string IndexName;
            public string[] Collections;

            public unsafe CacheKey(List<string> items)
            {
                _items = items;

                byte[] temp = null;
                var ctx = Hashing.Streamed.XXHash32.BeginProcess();
                foreach (var str in items)
                {
                    fixed (char* buffer = str)
                    {
                        var toProcess = str.Length;
                        var current = buffer;
                        do
                        {
                            if (toProcess < Hashing.Streamed.XXHash32.Alignment)
                            {
                                if (temp == null)
                                    temp = new byte[Hashing.Streamed.XXHash32.Alignment];

                                fixed (byte* tempBuffer = temp)
                                {
                                    Memory.Set(tempBuffer, 0, temp.Length);
                                    Memory.Copy(tempBuffer, (byte*)current, toProcess);

                                    ctx = Hashing.Streamed.XXHash32.Process(ctx, tempBuffer, temp.Length);
                                    break;
                                }
                            }

                            ctx = Hashing.Streamed.XXHash32.Process(ctx, (byte*)current, Hashing.Streamed.XXHash32.Alignment);
                            toProcess -= Hashing.Streamed.XXHash32.Alignment;
                            current += Hashing.Streamed.XXHash32.Alignment;
                        }
                        while (toProcess > 0);
                    }
                }
                _hash = (int)Hashing.Streamed.XXHash32.EndProcess(ctx);
            }

            public override bool Equals(object obj)
            {
                var cacheKey = obj as CacheKey;
                if (cacheKey != null)
                    return Equals(cacheKey);
                return false;
            }

            public bool Equals(CacheKey other)
            {
                if (_items.Count != other._items.Count)
                    return false;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i] != other._items[i])
                        return false;
                }
                return true;
            }

            public override int GetHashCode()
            {
                return _hash;
            }
        }
    }
}