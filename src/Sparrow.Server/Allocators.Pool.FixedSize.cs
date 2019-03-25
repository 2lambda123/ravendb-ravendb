﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sparrow.Global;

namespace Sparrow.Server
{
    public static class FixedSizePoolAllocator
    {
        public struct Default : IPoolAllocatorOptions, INativeOptions
        {
            public bool UseSecureMemory => false;
            public bool ElectricFenceEnabled => false;
            public bool Zeroed => false;

            public int MaxBlockSize => 1 * Constants.Size.Megabyte;
            public int MaxPoolSizeInBytes => 32 * Constants.Size.Megabyte;

            public bool HasOwnership => true;

            public IAllocatorComposer<Pointer> CreateAllocator()
            {
                var allocator = new Allocator<NativeAllocator<Default>>();
                allocator.Initialize(default(Default));
                return allocator;
            }

            /// <summary>
            /// By default whenever we create an allocator we are going to dispose it too when the time comes.
            /// </summary>
            /// <param name="allocator">the allocator to dispose.</param>
            public void ReleaseAllocator(IAllocatorComposer<Pointer> allocator, bool disposing)
            {
                allocator?.Dispose(disposing);
            }
        }
    }

    /// <summary>
    /// The PoolAllocator will hold all the memory it can during the process. It will not keep track of allocations (except when running in validation mode),
    /// that means this allocator can leak if used improperly. 
    /// </summary>
    /// <typeparam name="TOptions">The options to use for the allocator.</typeparam>
    /// <remarks>The Options object must be properly implemented to achieve performance improvements. (use constants as much as you can on configuration)</remarks>
    public unsafe struct FixedSizePoolAllocator<TOptions> : IAllocator<FixedSizePoolAllocator<TOptions>, Pointer>, ILowMemoryHandler<FixedSizePoolAllocator<TOptions>>, IRenewable<FixedSizePoolAllocator<TOptions>>
        where TOptions : struct, IPoolAllocatorOptions
    {
        private TOptions _options;
        private Pointer _freed;

        // PERF: This should be devirtualized.        
        private IAllocatorComposer<Pointer> _internalAllocator;

        public long TotalAllocated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        }

        public long Allocated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        }
        public long InUse
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        }

        public void Initialize(ref FixedSizePoolAllocator<TOptions> allocator)
        {
            // Initialize the struct pointers structure used to navigate over the allocated memory. 
            allocator._freed = new Pointer();

            allocator.TotalAllocated = 0;
            allocator.Allocated = 0;
            allocator.InUse = 0;
        }

        public void Configure<TConfig>(ref FixedSizePoolAllocator<TOptions> allocator, ref TConfig configuration) where TConfig : struct, IAllocatorOptions
        {
            if (!typeof(TOptions).GetTypeInfo().IsAssignableFrom(typeof(TConfig)))
                throw new NotSupportedException($"{nameof(TConfig)} is not compatible with {nameof(TOptions)}");

            // This cast will get evicted by the JIT.             
            allocator._options = (TOptions)(object)configuration;            
            allocator._internalAllocator = allocator._options.CreateAllocator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pointer Allocate(ref FixedSizePoolAllocator<TOptions> allocator, int size)
        {
            if (size != allocator._options.MaxBlockSize)
                ThrowOnlyFixedSizeMemoryCanBeRequested();

            if (allocator._freed.IsValid)
            {
                // Stack copy of the pointer itself.
                Pointer section = _freed;

                // Pointer was holding the marker for the next released block instead. 
                allocator._freed = *((Pointer*)section.Address);
                allocator.InUse += section.Size;
                allocator.TotalAllocated += section.Size;

                return section;
            }
            
            var ptr = _internalAllocator.Allocate(size);
            allocator.InUse += ptr.Size;
            allocator.Allocated += ptr.Size;
            allocator.TotalAllocated += ptr.Size;

            return new Pointer(ptr.Address, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Release(ref FixedSizePoolAllocator<TOptions> allocator, ref Pointer ptr)
        {
            if (ptr.Size != allocator._options.MaxBlockSize)
                ThrowMemoryDoesNotBelongToAllocator(ptr);

            if (allocator.InUse > allocator._options.MaxPoolSizeInBytes || Allocator.LowMemoryFlag.IsRaised())
                goto UnlikelyRelease;

            int originalSize = ptr.Size;

            var section = allocator._freed;
            if (section.IsValid)
            {
                // Copy the section pointer that is already freed to the current memory. 
                *(BlockPointer*)ptr.Address = section;
            }
            else
            {
                // Put a copy of the currently released memory block on the front. 
                *(BlockPointer*)ptr.Address = new BlockPointer();
            }

            // Put a copy of the currently released memory block on the front. 
            allocator._freed = ptr;
            allocator.InUse -= originalSize;

            ptr = new Pointer(); // Nullify the pointer
            return;

        UnlikelyRelease:
            // This should be an unlikely case, when you are running out of memory or over allocated,
            // all performance guarantees go down the drain. So we dont care if we hit expensive calls
            // that allows us to release some steam at the cost of hitting cold code. 
            // https://github.com/dotnet/coreclr/issues/6024

            allocator.InUse -= ptr.Size;
            allocator.Allocated -= ptr.Size;

            Pointer nakedPtr = new Pointer(ptr.Address, ptr.Size);
            allocator._internalAllocator.Release(ref nakedPtr);
        }

        private void ThrowMemoryDoesNotBelongToAllocator(in Pointer ptr)
        {
            throw new InvalidOperationException($"The memory pointer {ptr.Describe()} does not belong to this allocator.");
        }

        private void ThrowOnlyFixedSizeMemoryCanBeRequested()
        {
            throw new InvalidOperationException($"The memory size requested is not supported by this allocator. You should use {this._options.MaxBlockSize} instead.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Renew(ref FixedSizePoolAllocator<TOptions> allocator)
        {
            if (Allocator.LowMemoryFlag.IsRaised())
            {
                ReleaseMemoryPool(ref allocator);
                allocator.Allocated = 0;
            }                

            allocator._internalAllocator.Renew();
            allocator.InUse = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset(ref FixedSizePoolAllocator<TOptions> allocator)
        {
            if (allocator._options.HasOwnership)
                ReleaseMemoryPool(ref allocator);
            else
                ResetMemoryPool(ref allocator);

            allocator._internalAllocator.Reset();

            allocator.TotalAllocated = 0;
            allocator.Allocated = 0;
            allocator.InUse = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnAllocate(ref FixedSizePoolAllocator<TOptions> allocator, Pointer ptr)
        {
            // Nothing to do here.
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnRelease(ref FixedSizePoolAllocator<TOptions> allocator, Pointer ptr)
        {
            // Nothing to do here.
        }

        public void Dispose(ref FixedSizePoolAllocator<TOptions> allocator, bool disposing)
        {
            // We are going to be disposed, we then release all hoarded memory. 
            if (allocator._options.HasOwnership)
                allocator.ReleaseMemoryPool(ref allocator);

            // Dispose the internal allocator.
            allocator._options.ReleaseAllocator(allocator._internalAllocator, disposing);
        }

        private void ResetMemoryPool(ref FixedSizePoolAllocator<TOptions> allocator)
        {
            // We dont own the memory pool, so we just reset the state and let the owner give us memory again on the next cycle.
            // This is the typical mode of operation when the underlying allocator is able to reuse memory (ex. ArenaAllocator).
            allocator._freed = new Pointer();
        }

        private void ReleaseMemoryPool(ref FixedSizePoolAllocator<TOptions> allocator)
        {
            // We own the memory pool, so we have to release all the pointers that we have to the parent allocator.
            // This is the typical mode of operation when the underlying allocator is leaky (ex. NativeAllocator). 
            ref var section = ref _freed;
            while (section.IsValid)
            {
                Pointer current = section;

                // Copy the pointer found on the first memory bytes of the section. 
                section = *(Pointer*)current.Address;

                // The block is guaranteed to be valid, so we release it to the internal allocator.
                Pointer currentPtr = new Pointer(current.Address, current.Size);
                allocator._internalAllocator.Release(ref currentPtr);
            }
        }

        public void NotifyLowMemory(ref FixedSizePoolAllocator<TOptions> allocator)
        {
            // We are told that we are low in memory, therefore if we own the memory we will release it.
            if (allocator._options.HasOwnership)
                allocator.ReleaseMemoryPool(ref allocator);

            allocator._internalAllocator.LowMemory();
        }

        public void NotifyLowMemoryOver(ref FixedSizePoolAllocator<TOptions> allocator)
        {
            allocator._internalAllocator.LowMemoryOver();
        }

        public bool IsThreadSafe => false;
    }
}
