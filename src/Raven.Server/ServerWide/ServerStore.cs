﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Framework.ConfigurationModel;
using Raven.Abstractions.Logging;
using Raven.Server.Config;
using Raven.Server.Documents;
using Raven.Server.Json;
using Raven.Server.ServerWide.LowMemoryNotification;
using Raven.Server.Utils;
using Sparrow.Platform;
using Voron;
using Voron.Data;
using Voron.Data.BTrees;

namespace Raven.Server.ServerWide
{
    /// <summary>
    /// Persistent store for server wide configuration, such as cluster settings, database configuration, etc
    /// </summary>
    public unsafe class ServerStore : IDisposable
    {
        private CancellationTokenSource shutdownNotification;

        public CancellationToken ServerShutdown => shutdownNotification.Token;

        private static readonly ILog Log = LogManager.GetLogger(typeof(ServerStore));

        private StorageEnvironment _env;

        private UnmanagedBuffersPool _pool;

        public readonly DatabasesLandlord DatabasesLandlord;

        private readonly IList<IDisposable> toDispose = new List<IDisposable>();
        public readonly RavenConfiguration Configuration;

        public ServerStore(RavenConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            Configuration = configuration;
            
            DatabasesLandlord = new DatabasesLandlord(this);
        }

        public ContextPool ContextPool;

        public void Initialize()
        {
            shutdownNotification = new CancellationTokenSource();

            AbstractLowMemoryNotification.Initialize(ServerShutdown, Configuration);

            if (Log.IsDebugEnabled)
            {
                Log.Debug("Starting to open server store for {0}", Configuration.Core.RunInMemory ? "<memory>" : Configuration.Core.DataDirectory);
            }
            var options = Configuration.Core.RunInMemory
                ? StorageEnvironmentOptions.CreateMemoryOnly()
                : StorageEnvironmentOptions.ForPath(Configuration.Core.DataDirectory);

            options.SchemaVersion = 1;

            try
            {
                _env = new StorageEnvironment(options);
                using (var tx = _env.WriteTransaction())
                {
                    tx.CreateTree("items");
                    tx.Commit();
                }
            }
            catch (Exception e)
            {
                if (Log.IsWarnEnabled)
                {
                    Log.FatalException(
                        "Could not open server store for " + (Configuration.Core.RunInMemory ? "<memory>" : Configuration.Core.DataDirectory), e);
                }
                options.Dispose();
                throw;
            }

            _pool = new UnmanagedBuffersPool("ServerStore");// 128MB should be more than big enough for the server store
            ContextPool = new ContextPool(_pool, _env);
        }

        public BlittableJsonReaderObject Read(RavenOperationContext ctx, string id)
        {
            var dbs = ctx.Transaction.ReadTree("items");
            var result = dbs.Read(id);
            if (result == null)
                return null;
            return new BlittableJsonReaderObject(result.Reader.Base, result.Reader.Length, ctx);
        }

        public class Item
        {
            public string Key;
            public BlittableJsonReaderObject Data;
        }

        public IEnumerable<Item> StartingWith(RavenOperationContext ctx, string prefix, int start, int take)
        {
            var dbs = ctx.Transaction.ReadTree("items");
            using (var it = dbs.Iterate())
            {
                it.RequiredPrefix = prefix;
                if(it.Seek(it.RequiredPrefix) == false)
                    yield break;

                do
                {
                    if (start > 0)
                    {
                        start--;
                        continue;
                    }
                    if (take-- <= 0)
                        yield break;

                    yield return GetCurrentItem(ctx, it);
                } while (it.MoveNext());
            }
        }

        private static Item GetCurrentItem(RavenOperationContext ctx, IIterator it)
        {
            var readerForCurrent = it.CreateReaderForCurrent();
            return new Item
            {
                Data = new BlittableJsonReaderObject(readerForCurrent.Base, readerForCurrent.Length, ctx),
                Key = it.CurrentKey.ToString()
            };
        }


        public void Write(RavenOperationContext ctx, string id, BlittableJsonReaderObject doc)
        {
            var dbs = ctx.Transaction.ReadTree("items");

            var ptr = dbs.DirectAdd(id, doc.Size);
            doc.CopyTo(ptr);
        }

        public void Dispose()
        {
            shutdownNotification.Cancel();

            ContextPool?.Dispose();

            toDispose.Add(_pool);
            toDispose.Add(_env);
            toDispose.Add(DatabasesLandlord);

            var errors = new List<Exception>();
            foreach (var disposable in toDispose)
            {
                try
                {
                    disposable?.Dispose();
                }
                catch (Exception e)
                {
                    errors.Add(e);
                }
            }
            if (errors.Count != 0)
                throw new AggregateException(errors);
        }
    }
}