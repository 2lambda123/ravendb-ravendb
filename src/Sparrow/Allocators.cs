﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sparrow.Binary;
using Sparrow.Global;
using Sparrow.LowMemory;
using Sparrow.Threading;

namespace Sparrow
{
    public interface IAllocator {}

    public interface IAllocatorOptions {}

    public interface ILifecycleHandler<TAllocator> where TAllocator : struct, IAllocator, IDisposable
    {
        void BeforeInitialize(ref TAllocator allocator);
        void AfterInitialize(ref TAllocator allocator);
        void BeforeDispose(ref TAllocator allocator);
        void BeforeFinalization(ref TAllocator allocator);
    }

    public interface ILowMemoryHandler<TAllocator> where TAllocator : struct, IAllocator, IDisposable
    {
        void NotifyLowMemory(ref TAllocator allocator);
        void NotifyLowMemoryOver(ref TAllocator allocator);
    }

    public interface IRenewable<TAllocator> where TAllocator : struct, IAllocator, IDisposable
    {
        void Renew(ref TAllocator allocator);
    }

    public interface IAllocatorComposer<TPointerType> where TPointerType : struct, IPointerType
    {
        void Initialize<TAllocatorOptions>(TAllocatorOptions options) where TAllocatorOptions : struct, IAllocatorOptions;

        TPointerType Allocate(int size);       
        void Release(ref TPointerType ptr);
    }

    public interface IAllocator<T, TPointerType> 
        where T : struct, IAllocator, IDisposable
        where TPointerType : struct, IPointerType
    {
        int Allocated { get; }

        void Initialize(ref T allocator);

        void Configure<TConfig>(ref T allocator, ref TConfig configuration) where TConfig : struct, IAllocatorOptions;

        TPointerType Allocate(ref T allocator, int size);
        void Release(ref T allocator, ref TPointerType ptr);
        void Reset(ref T allocator);

        void OnAllocate(ref T allocator, TPointerType ptr);
        void OnRelease(ref T allocator, TPointerType ptr);
    }


    public sealed class BlockAllocator<TAllocator> : IAllocatorComposer<BlockPointer>, IDisposable, ILowMemoryHandler
        where TAllocator : struct, IAllocator<TAllocator, BlockPointer>, IAllocator, IDisposable
    {
        private TAllocator _allocator;
        private readonly SingleUseFlag _disposeFlag = new SingleUseFlag();

        ~BlockAllocator()
        {
            if (_allocator is ILifecycleHandler<TAllocator> a)
                a.BeforeFinalization(ref _allocator);

            Dispose();
        }

        public void Initialize<TBlockAllocatorOptions>(TBlockAllocatorOptions options)
            where TBlockAllocatorOptions : struct, IAllocatorOptions
        {

            if (_allocator is ILifecycleHandler<TAllocator> a)
                a.BeforeInitialize(ref _allocator);

            _allocator.Initialize(ref _allocator);
            _allocator.Configure(ref _allocator, ref options);

            if (_allocator is ILifecycleHandler<TAllocator> b)
                b.AfterInitialize(ref _allocator);
        }

        public int Allocated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _allocator.Allocated; }
        }
        
        public BlockPointer Allocate(int size)
        {
            unsafe
            {
                var ptr = _allocator.Allocate(ref _allocator, size);
                if (_allocator is ILifecycleHandler<TAllocator> a)
                    a.BeforeInitialize(ref _allocator);

                return ptr;
            }
        }

        public BlockPointer<TType> Allocate<TType>(int size) where TType : struct
        {
            unsafe
            {
                var ptr = _allocator.Allocate(ref _allocator, size * Unsafe.SizeOf<TType>());
                
                // PERF: We cannot make this conditional because the runtime cost would kill us (too much traffic).
                //       But we can call it anyways and use the capability of evicting the call if empty.
                _allocator.OnAllocate(ref _allocator, ptr);

                return new BlockPointer<TType>(ptr);
            }
        }

        public void Release<TType>(ref BlockPointer<TType> ptr) where TType : struct
        {
            unsafe
            {
                // PERF: We cannot make this conditional because the runtime cost would kill us (too much traffic).
                //       But we can call it anyways and use the capability of evicting the call if empty.
                _allocator.OnRelease(ref _allocator, ptr);

                var localRef = ptr._ptr;
                _allocator.Release(ref _allocator, ref localRef);

                ptr = new BlockPointer<TType>();
            }
        }

        public void Release(ref BlockPointer ptr)
        {
            unsafe
            {
                // PERF: We cannot make this conditional because the runtime cost would kill us (too much traffic).
                //       But we can call it anyways and use the capability of evicting the call if empty.
                _allocator.OnRelease(ref _allocator, ptr);

                _allocator.Release(ref _allocator, ref ptr);

                ptr = new BlockPointer();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Renew()
        {
            if (_allocator is IRenewable<TAllocator> a)
                a.Renew(ref _allocator);
            else
                throw new NotSupportedException($".{nameof(Renew)}() is not supported for this allocator type.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _allocator.Reset(ref _allocator);
        }
        
        public void Dispose()
        {
            if (_disposeFlag.Raise())
                _allocator.Dispose();

            GC.SuppressFinalize(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LowMemory()
        {
            if (_allocator is ILowMemoryHandler<TAllocator> a)
                a.NotifyLowMemory(ref _allocator);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LowMemoryOver()
        {
            if (_allocator is ILowMemoryHandler<TAllocator> a)
                a.NotifyLowMemoryOver(ref _allocator);
        }
    }
}
