﻿using System;
using Sparrow.Json;
using Sparrow.Server;
using Sparrow.Threading;
using Voron;
using Voron.Util;

namespace Raven.Server.ServerWide.Context
{
    public class TransactionOperationContext : TransactionOperationContext<RavenTransaction>
    {
        private readonly StorageEnvironment _environment;

        public bool IgnoreStalenessDueToReduceOutputsToDelete;

        public TransactionOperationContext(StorageEnvironment environment, int initialSize, int longLivedSize, int maxNumberOfAllocatedStringValues, SharedMultipleUseFlag lowMemoryFlag)
            : base(initialSize, longLivedSize, maxNumberOfAllocatedStringValues, lowMemoryFlag)
        {
            _environment = environment;
        }

        protected override RavenTransaction CloneReadTransaction(RavenTransaction previous)
        {
            var clonedTx = new RavenTransaction(_environment.CloneReadTransaction(previous.InnerTransaction, PersistentContext, Allocator));

            previous.Dispose();

            return clonedTx;
        }

        protected override RavenTransaction CreateReadTransaction()
        {
            return new RavenTransaction(_environment.ReadTransaction(PersistentContext, Allocator));
        }

        protected override RavenTransaction CreateWriteTransaction(TimeSpan? timeout = null)
        {
            return new RavenTransaction(_environment.WriteTransaction(PersistentContext, Allocator, timeout));
        }

        public StorageEnvironment Environment => _environment;
    }

    public abstract class TransactionOperationContext<TTransaction> : JsonOperationContext
        where TTransaction : RavenTransaction
    {
        public readonly ByteStringContext Allocator;
        public readonly TransactionPersistentContext PersistentContext;

        public TTransaction Transaction;

        protected TransactionOperationContext(int initialSize, int longLivedSize, int maxNumberOfAllocatedStringValues, SharedMultipleUseFlag lowMemoryFlag)
            : base(initialSize, longLivedSize, maxNumberOfAllocatedStringValues, lowMemoryFlag)
        {
            PersistentContext = new TransactionPersistentContext();
            Allocator = new ByteStringContext(lowMemoryFlag);
        }

        public TTransaction OpenReadTransaction()
        {
            if (Transaction != null && Transaction.Disposed == false)
                ThrowTransactionAlreadyOpened();

            Transaction = CreateReadTransaction();

            return Transaction;
        }

        public TTransaction CloneReadTransaction()
        {
            if (Transaction == null || Transaction.Disposed || Transaction.InnerTransaction.IsWriteTransaction)
                ThrowReadTransactionMustBeOpen();

            Transaction = CloneReadTransaction(Transaction);

            return Transaction;
        }

        protected abstract TTransaction CloneReadTransaction(TTransaction previous);

        public bool HasTransaction => Transaction != null && Transaction.Disposed == false;

        public short TransactionMarkerOffset;

        protected short CurrentTxMarker;

        public short GetTransactionMarker()
        {
            if (Transaction != null && Transaction.Disposed == false && Transaction.InnerTransaction.LowLevelTransaction.Flags != TransactionFlags.ReadWrite)
                ThrowWriteTransactionMustBeOpen();

            var value = (short)(CurrentTxMarker + TransactionMarkerOffset);

            if (value == 0)
                return 2;
            if (value < 0)
                return (short)-value;

            return value;
        }

        private static void ThrowWriteTransactionMustBeOpen()
        {
            throw new InvalidOperationException("Write transaction must be opened");
        }

        private static void ThrowReadTransactionMustBeOpen()
        {
            throw new InvalidOperationException("Read transaction must be opened");
        }

        protected abstract TTransaction CreateReadTransaction();

        protected abstract TTransaction CreateWriteTransaction(TimeSpan? timeout = null);

        public TTransaction OpenWriteTransaction(TimeSpan? timeout = null)
        {
            if (Transaction != null && Transaction.Disposed == false)
            {
                ThrowTransactionAlreadyOpened();
            }

            Transaction = CreateWriteTransaction(timeout);

            return Transaction;
        }

        private static void ThrowTransactionAlreadyOpened()
        {
            throw new InvalidOperationException("Transaction is already opened");
        }

        private readonly MultipleUseFlag _locked = new MultipleUseFlag();
        private Action _continuation;

        public IDisposable PreventContextReturn()
        {
            _locked.RaiseOrDie();

            return new DisposableAction(()=>
            {
                _locked.LowerOrDie();
                _continuation?.Invoke();
            });
        }

        public void CloseTransaction()
        {
            Transaction?.Dispose();
            Transaction = null;
        }

        public override void Dispose()
        {
            if (_locked.IsRaised())
            {
                _continuation = Dispose;
                return;
            }

            base.Dispose();

            Allocator.Dispose();
        }

        protected internal override void Reset(bool forceResetLongLivedAllocator = false, bool releaseAllocatedStringValues = false)
        {
            if (_locked.Raise() == false)
            {
                _continuation = () => Reset(forceResetLongLivedAllocator, releaseAllocatedStringValues);
                return;
            }

            CloseTransaction();

            base.Reset(forceResetLongLivedAllocator, releaseAllocatedStringValues);

            Allocator.Reset();

            _locked.Lower();
        }
    }
}
