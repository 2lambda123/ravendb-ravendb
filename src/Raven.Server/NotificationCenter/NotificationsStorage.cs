﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Raven.Client.Util;
using Raven.Server.Json;
using Raven.Server.NotificationCenter.Notifications;
using Raven.Server.NotificationCenter.Notifications.Details;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow;
using Sparrow.Binary;
using Sparrow.Json;
using Sparrow.Logging;
using Sparrow.Server.Utils;
using Voron;
using Voron.Data.Tables;

namespace Raven.Server.NotificationCenter
{
    public sealed unsafe class NotificationsStorage
    {
        private readonly string _tableName;

        private readonly Logger Logger;

        private StorageEnvironment _environment;

        private TransactionContextPool _contextPool;

        public NotificationsStorage(string resourceName = null)
        {
            _tableName = GetTableName(resourceName);

            Logger = LoggingSource.Instance.GetLogger<NotificationsStorage>(resourceName);
        }

        public void Initialize(StorageEnvironment environment, TransactionContextPool contextPool)
        {
            _environment = environment;
            _contextPool = contextPool;

            using (contextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (var tx = _environment.WriteTransaction(context.PersistentContext))
            {
                Documents.Schemas.Notifications.Current.Create(tx, _tableName, 16);

                tx.Commit();
            }

            Cleanup();
        }

        public bool Store(Notification notification, DateTime? postponeUntil = null, bool updateExisting = true)
        {
            using (_contextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                using (var tx = context.OpenReadTransaction())
                {
                    // if previous notification had postponed until value pass this value to newly saved notification
                    using (var existing = Get(notification.Id, context, tx))
                    {
                        if (existing != null && updateExisting == false)
                            return false;

                        if (postponeUntil == null)
                        {
                            if (existing?.PostponedUntil == DateTime.MaxValue) // postponed until forever
                                return false;

                            if (existing?.PostponedUntil != null && existing.PostponedUntil.Value > SystemTime.UtcNow)
                                postponeUntil = existing.PostponedUntil;
                        }
                    }
                }

                if (Logger.IsInfoEnabled)
                    Logger.Info($"Saving notification '{notification.Id}'.");

                using (var json = context.ReadObject(notification.ToJson(), "notification", BlittableJsonDocumentBuilder.UsageMode.ToDisk))
                using (var tx = context.OpenWriteTransaction())
                {
                    Store(context.GetLazyString(notification.Id), notification.CreatedAt, postponeUntil, json, tx);
                    tx.Commit();
                }
            }

            return true;
        }

        private readonly long _postponeDateNotSpecified = Bits.SwapBytes(long.MaxValue);

        internal void Store(LazyStringValue id, DateTime createdAt, DateTime? postponedUntil, BlittableJsonReaderObject action, RavenTransaction tx)
        {
            var table = tx.InnerTransaction.OpenTable(Documents.Schemas.Notifications.Current, _tableName);

            var createdAtTicks = Bits.SwapBytes(createdAt.Ticks);

            var postponedUntilTicks = postponedUntil != null
                ? Bits.SwapBytes(postponedUntil.Value.Ticks)
                : _postponeDateNotSpecified;

            using (table.Allocate(out TableValueBuilder tvb))
            {
                tvb.Add(id.Buffer, id.Size);
                tvb.Add((byte*)&createdAtTicks, sizeof(long));
                tvb.Add((byte*)&postponedUntilTicks, sizeof(long));
                tvb.Add(action.BasePointer, action.Size);

                table.Set(tvb);
            }
        }

        public IDisposable ReadActionsOrderedByCreationDate(out IEnumerable<NotificationTableValue> actions)
        {
            using (var scope = new DisposableScope())
            {
                scope.EnsureDispose(_contextPool.AllocateOperationContext(out TransactionOperationContext context));
                scope.EnsureDispose(context.OpenReadTransaction());

                actions = ReadActionsByCreatedAtIndex(context);

                return scope.Delay();
            }
        }

        public IDisposable Read(string id, out NotificationTableValue value)
        {
            using (var scope = new DisposableScope())
            {
                RavenTransaction tx;

                scope.EnsureDispose(_contextPool.AllocateOperationContext(out TransactionOperationContext context));
                scope.EnsureDispose(tx = context.OpenReadTransaction());

                value = Get(id, context, tx);

                return scope.Delay();
            }
        }

        private IEnumerable<NotificationTableValue> ReadActionsByCreatedAtIndex(TransactionOperationContext context)
        {
            var table = context.Transaction.InnerTransaction.OpenTable(Documents.Schemas.Notifications.Current, _tableName);
            if (table == null)
                yield break;

            foreach (var tvr in table.SeekForwardFrom(Documents.Schemas.Notifications.Current.Indexes[Documents.Schemas.Notifications.ByCreatedAt], Slices.BeforeAllKeys, 0))
            {
                yield return Read(context, ref tvr.Result.Reader);
            }
        }

        public IDisposable ReadPostponedActions(out IEnumerable<NotificationTableValue> actions, DateTime cutoff)
        {
            using (var scope = new DisposableScope())
            {
                scope.EnsureDispose(_contextPool.AllocateOperationContext(out TransactionOperationContext context));
                scope.EnsureDispose(context.OpenReadTransaction());

                actions = ReadPostponedActionsByPostponedUntilIndex(context, cutoff);

                return scope.Delay();
            }
        }

        private IEnumerable<NotificationTableValue> ReadPostponedActionsByPostponedUntilIndex(TransactionOperationContext context, DateTime cutoff)
        {
            var table = context.Transaction.InnerTransaction.OpenTable(Documents.Schemas.Notifications.Current, _tableName);
            if (table == null)
                yield break;

            foreach (var tvr in table.SeekForwardFrom(Documents.Schemas.Notifications.Current.Indexes[Documents.Schemas.Notifications.ByPostponedUntil], Slices.BeforeAllKeys, 0))
            {
                var action = Read(context, ref tvr.Result.Reader);

                if (action.PostponedUntil == null)
                {
                    action.Dispose();
                    continue;
                }

                if (action.PostponedUntil > cutoff)
                {
                    action.Dispose();
                    break;
                }

                if (action.PostponedUntil == DateTime.MaxValue)
                {
                    action.Dispose();
                    break;
                }

                yield return action;
            }
        }

        private NotificationTableValue Get(string id, JsonOperationContext context, RavenTransaction tx)
        {
            var table = tx.InnerTransaction.OpenTable(Documents.Schemas.Notifications.Current, _tableName);
            if (table == null)
                return null;

            using (Slice.From(tx.InnerTransaction.Allocator, id, out Slice slice))
            {
                if (table.ReadByKey(slice, out TableValueReader tvr) == false)
                    return null;

                return Read(context, ref tvr);
            }
        }

        public bool Delete(string id, RavenTransaction existingTransaction = null)
        {
            bool deleteResult;

            if (existingTransaction != null)
            {
                deleteResult = DeleteFromTable(existingTransaction);
            }
            else
            {
                using (_contextPool.AllocateOperationContext(out TransactionOperationContext context))
                using (var tx = context.OpenWriteTransaction())
                {
                    deleteResult = DeleteFromTable(tx);
                    tx.Commit();
                }
            }

            if (deleteResult && Logger.IsInfoEnabled)
                Logger.Info($"Deleted notification '{id}'.");

            return deleteResult;

            bool DeleteFromTable(RavenTransaction tx)
            {
                var table = tx.InnerTransaction.OpenTable(Documents.Schemas.Notifications.Current, _tableName);

                using (Slice.From(tx.InnerTransaction.Allocator, id, out Slice alertSlice))
                {
                    return table.DeleteByKey(alertSlice);
                }
            }
        }

        public bool Exists(string id)
        {
            using (_contextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (var tx = context.OpenReadTransaction())
            using (Slice.From(tx.InnerTransaction.Allocator, id, out Slice slice))
            {
                var table = tx.InnerTransaction.OpenTable(Documents.Schemas.Notifications.Current, _tableName);
                if (table == null)
                    return false;

                return table.ReadByKey(slice, out _);
            }
        }

        public long GetAlertCount()
        {
            return GetNotificationCount(nameof(NotificationType.AlertRaised));
        }

        public long GetPerformanceHintCount()
        {
            return GetNotificationCount(nameof(NotificationType.PerformanceHint));
        }

        private long GetNotificationCount(string notificationType)
        {
            var count = 0;

            using (_contextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                foreach (var action in ReadActionsByCreatedAtIndex(context))
                {
                    using (action)
                    {
                        if (action.Json.TryGetMember(nameof(Notification.Type), out object type) == false)
                            ThrowCouldNotFindNotificationType(action);

                        var typeLsv = (LazyStringValue)type;

                        if (typeLsv.CompareTo(notificationType) == 0)
                            count++;
                    }
                }
            }

            return count;
        }

        private NotificationTableValue Read(JsonOperationContext context, ref TableValueReader reader)
        {
            var createdAt = new DateTime(Bits.SwapBytes(*(long*)reader.Read(Documents.Schemas.Notifications.NotificationsTable.CreatedAtIndex, out int size)));

            var postponeUntilTicks = *(long*)reader.Read(Documents.Schemas.Notifications.NotificationsTable.PostponedUntilIndex, out size);

            DateTime? postponedUntil = null;
            if (postponeUntilTicks != _postponeDateNotSpecified)
                postponedUntil = new DateTime(Bits.SwapBytes(postponeUntilTicks));

            var jsonPtr = reader.Read(Documents.Schemas.Notifications.NotificationsTable.JsonIndex, out size);

            return new NotificationTableValue
            {
                CreatedAt = createdAt,
                PostponedUntil = postponedUntil,
                Json = new BlittableJsonReaderObject(jsonPtr, size, context)
            };
        }

        public string GetDatabaseFor(string id)
        {
            using (_contextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (var tx = context.OpenReadTransaction())
            {
                using (var item = Get(id, context, tx))
                {
                    if (item == null)
                        return null;
                    item.Json.TryGet("Database", out string db);
                    return db;
                }
            }
        }

        public void ChangePostponeDate(string id, DateTime? postponeUntil)
        {
            using (_contextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (var tx = context.OpenWriteTransaction())
            {
                using (var item = Get(id, context, tx))
                {
                    if (item == null)
                        return;

                    var itemCopy = context.GetMemory(item.Json.Size);

                    Memory.Copy(itemCopy.Address, item.Json.BasePointer, item.Json.Size);

                    Store(context.GetLazyString(id), item.CreatedAt, postponeUntil,
                        //we create a copy because we can't update directly from mutated memory
                        new BlittableJsonReaderObject(itemCopy.Address, item.Json.Size, context)
                        , tx);

                    tx.Commit();
                }
            }
        }

        private void Cleanup()
        {
            RemoveNewVersionAvailableAlertIfNecessary();
        }

        private static string GetTableName(string resourceName)
        {
            return string.IsNullOrEmpty(resourceName)
                ? Documents.Schemas.Notifications.NotificationsTree
                : $"{Documents.Schemas.Notifications.NotificationsTree}.{resourceName.ToLowerInvariant()}";
        }

        private void RemoveNewVersionAvailableAlertIfNecessary()
        {
            var buildNumber = ServerVersion.Build;

            var id = AlertRaised.GetKey(AlertType.Server_NewVersionAvailable, null);
            using (Read(id, out var ntv))
            {
                using (ntv)
                {
                    if (ntv == null)
                        return;

                    var delete = true;

                    if (buildNumber != ServerVersion.DevBuildNumber)
                    {
                        if (ntv.Json.TryGetMember(nameof(AlertRaised.Details), out var o)
                            && o is BlittableJsonReaderObject detailsJson)
                        {
                            if (detailsJson.TryGetMember(nameof(NewVersionAvailableDetails.VersionInfo), out o)
                                && o is BlittableJsonReaderObject newVersionDetailsJson)
                            {
                                var value = JsonDeserializationServer.LatestVersionCheckVersionInfo(newVersionDetailsJson);
                                delete = value.BuildNumber <= buildNumber;
                            }
                        }
                    }

                    if (delete)
                        Delete(id);
                }
            }
        }

        [DoesNotReturn]
        private static void ThrowCouldNotFindNotificationType(NotificationTableValue action)
        {
            string notificationJson;

            try
            {
                notificationJson = action.Json.ToString();
            }
            catch (Exception e)
            {
                notificationJson = $"invalid json - {e.Message}";
            }

            throw new InvalidOperationException(
                $"Could not find notification type. Notification: {notificationJson}, created at: {action.CreatedAt}, postponed until: {action.PostponedUntil}");
        }

        public NotificationsStorage GetStorageFor(string database)
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            var storage = new NotificationsStorage(database);
            storage.Initialize(_environment, _contextPool);

            return storage;
        }

        public void DeleteStorageFor(string database)
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            var tableName = GetTableName(database);

            using (var tx = _environment.WriteTransaction())
            {
                tx.DeleteTable(tableName);

                tx.Commit();
            }
        }

        public void DeleteStorageFor<T>(TransactionOperationContext<T> ctx, string database) where T : RavenTransaction
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            var tableName = GetTableName(database);
            ctx.Transaction.InnerTransaction.DeleteTable(tableName);
        }
    }
}
