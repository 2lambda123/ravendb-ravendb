﻿using System;
using System.Collections.Concurrent;
using Sparrow.Binary;
using Sparrow.LowMemory;
using Sparrow.Server;
using Sparrow.Server.Platform;
using Sparrow.Utils;
using Constants = Voron.Global.Constants;

namespace Voron.Impl
{
    public unsafe class EncryptionBuffersPool : ILowMemoryHandler
    {
        public static EncryptionBuffersPool Instance = new EncryptionBuffersPool();

        private class NativeAllocation
        {
            public IntPtr Ptr;
            public long Size;
            public NativeMemory.ThreadStats AllocatingThread;
        }

        private readonly ConcurrentStack<NativeAllocation>[] _items;

        public EncryptionBuffersPool()
        {
            _items = new ConcurrentStack<NativeAllocation>[32];
            for (int i = 0; i < _items.Length; i++)
            {
                _items[i] = new ConcurrentStack<NativeAllocation>();
            }

            LowMemoryNotification.Instance.RegisterLowMemoryHandler(this);
        }

        public byte* Get(int size, out NativeMemory.ThreadStats thread)
        {
            size = Bits.PowerOf2(size);

            if (size > Constants.Size.Megabyte * 16)
            {
                // We don't want to pool large buffers
                return PlatformSpecific.NativeMemory.Allocate4KbAlignedMemory(size, out thread);
            }

            var index = Bits.MostSignificantBit(size);

            if (_items[index].TryPop(out var allocation))
            {
                thread = allocation.AllocatingThread;
                return (byte*)allocation.Ptr;
            }

            return PlatformSpecific.NativeMemory.Allocate4KbAlignedMemory(size, out thread);
        }

        public void Return(byte* ptr, long size, NativeMemory.ThreadStats allocatingThread)
        {
            if (ptr == null)
                return;

            size = Bits.PowerOf2(size);
            Sodium.sodium_memzero(ptr, (UIntPtr)size);

            if (size > Constants.Size.Megabyte * 16 || LowMemoryNotification.Instance.LowMemoryState)
            {
                // We don't want to pool large buffers / clear them up on low memory
                PlatformSpecific.NativeMemory.Free4KbAlignedMemory(ptr, size, allocatingThread);
                return;
            }

            var index = Bits.MostSignificantBit(size);
            _items[index].Push(new NativeAllocation
            {
                Ptr = (IntPtr)ptr,
                AllocatingThread = allocatingThread,
                Size = size
            });
        }

        public void ReleaseUnmanagedResources()
        {
            foreach (var stack in _items)
            {
                while (stack.TryPop(out var allocation))
                {
                    PlatformSpecific.NativeMemory.Free4KbAlignedMemory((byte*)allocation.Ptr, allocation.Size, allocation.AllocatingThread);
                }
            }
        }

        public void LowMemory(LowMemorySeverity lowMemorySeverity)
        {
            if (lowMemorySeverity != LowMemorySeverity.ExtremelyLow)
                return;

            ReleaseUnmanagedResources();
        }

        public void LowMemoryOver()
        {
        }
    }
}
