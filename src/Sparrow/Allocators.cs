﻿using System;
using System.Runtime.CompilerServices;
using Sparrow.LowMemory;
using Sparrow.Threading;

namespace Sparrow
{
    public interface IAllocator {}

    public interface IBlockAllocator : IAllocator { }

    public interface IAllocatorOptions { }

    public interface IBlockAllocatorOptions : IAllocatorOptions
    {
        int BlockSize { get; }
    }

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

    public interface IBlockAllocator<T, TPointerType>
        where T : struct, IBlockAllocator, IDisposable
        where TPointerType : struct, IPointerType
    {
        int Allocated { get; }

        void Initialize(ref T allocator);

        void Configure<TConfig>(ref T allocator, ref TConfig configuration) where TConfig : struct, IAllocatorOptions;

        TPointerType Allocate(ref T allocator);
        void Release(ref T allocator, ref TPointerType ptr);
        void Reset(ref T allocator);

        void OnAllocate(ref T allocator, TPointerType ptr);
        void OnRelease(ref T allocator, TPointerType ptr);
    }


    public sealed class Allocator<TAllocator> : IAllocatorComposer<Pointer>, IDisposable, ILowMemoryHandler
        where TAllocator : struct, IAllocator<TAllocator, Pointer>, IAllocator, IDisposable
    {
        private TAllocator _allocator;
        private readonly SingleUseFlag _disposeFlag = new SingleUseFlag();

        ~Allocator()
        {
            if (_allocator is ILifecycleHandler<TAllocator> a)
                a.BeforeFinalization(ref _allocator);

            Dispose();
        }

        public void Initialize<TAllocatorOptions>(TAllocatorOptions options)
            where TAllocatorOptions : struct, IAllocatorOptions
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

        public Pointer Allocate(int size)
        {
            unsafe
            {
                var ptr = _allocator.Allocate(ref _allocator, size);
                if (_allocator is ILifecycleHandler<TAllocator> a)
                    a.BeforeInitialize(ref _allocator);

                return ptr;
            }
        }

        public Pointer<TType> Allocate<TType>(int size) where TType : struct
        {
            unsafe
            {
                var ptr = _allocator.Allocate(ref _allocator, size * Unsafe.SizeOf<TType>());

                // PERF: We cannot make this conditional because the runtime cost would kill us (too much traffic).
                //       But we can call it anyways and use the capability of evicting the call if empty.
                _allocator.OnAllocate(ref _allocator, ptr);

                return new Pointer<TType>(ptr);
            }
        }

        public void Release<TType>(ref Pointer<TType> ptr) where TType : struct
        {
            unsafe
            {
                // PERF: We cannot make this conditional because the runtime cost would kill us (too much traffic).
                //       But we can call it anyways and use the capability of evicting the call if empty.
                _allocator.OnRelease(ref _allocator, ptr);

                Pointer localPtr = ptr;
                _allocator.Release(ref _allocator, ref localPtr);

                ptr = new Pointer<TType>();
            }
        }

        public void Release(ref Pointer ptr)
        {
            unsafe
            {
                // PERF: We cannot make this conditional because the runtime cost would kill us (too much traffic).
                //       But we can call it anyways and use the capability of evicting the call if empty.
                _allocator.OnRelease(ref _allocator, ptr);

                _allocator.Release(ref _allocator, ref ptr);

                ptr = new Pointer();
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

    public sealed class BlockAllocator<TAllocator> : IDisposable, ILowMemoryHandler
        where TAllocator : struct, IAllocator<TAllocator, Pointer>, IAllocator, IDisposable
    {
        private int _blockSize;
        private TAllocator _allocator;
        private readonly SingleUseFlag _disposeFlag = new SingleUseFlag();

        ~BlockAllocator()
        {
            if (_allocator is ILifecycleHandler<TAllocator> a)
                a.BeforeFinalization(ref _allocator);

            Dispose();
        }

        public void Initialize<TConfig>(TConfig options)
            where TConfig : struct, IAllocatorOptions
        {

            if (!typeof(IBlockAllocatorOptions).IsAssignableFrom(typeof(TConfig)))
                throw new NotSupportedException($"{nameof(TConfig)} is not compatible with {nameof(TConfig)}");

            this._blockSize = ((IBlockAllocatorOptions)options).BlockSize;

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

        public Pointer Allocate()
        {
            unsafe
            {
                var ptr = _allocator.Allocate(ref _allocator, this._blockSize);
                if (_allocator is ILifecycleHandler<TAllocator> a)
                    a.BeforeInitialize(ref _allocator);

                return ptr;
            }
        }

        public Pointer<TType> Allocate<TType>() where TType : struct
        {
            unsafe
            {
                var ptr = _allocator.Allocate(ref _allocator, this._blockSize * Unsafe.SizeOf<TType>());

                // PERF: We cannot make this conditional because the runtime cost would kill us (too much traffic).
                //       But we can call it anyways and use the capability of evicting the call if empty.
                _allocator.OnAllocate(ref _allocator, ptr);

                return new Pointer<TType>(ptr);
            }
        }

        public void Release<TType>(ref Pointer<TType> ptr) where TType : struct
        {
            unsafe
            {
                // PERF: We cannot make this conditional because the runtime cost would kill us (too much traffic).
                //       But we can call it anyways and use the capability of evicting the call if empty.
                _allocator.OnRelease(ref _allocator, ptr);

                Pointer localPtr = ptr;
                _allocator.Release(ref _allocator, ref localPtr);

                ptr = new Pointer<TType>();
            }
        }

        public void Release(ref Pointer ptr)
        {
            unsafe
            {
                // PERF: We cannot make this conditional because the runtime cost would kill us (too much traffic).
                //       But we can call it anyways and use the capability of evicting the call if empty.
                _allocator.OnRelease(ref _allocator, ptr);

                _allocator.Release(ref _allocator, ref ptr);

                ptr = new Pointer();
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
