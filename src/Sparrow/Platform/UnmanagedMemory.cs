﻿using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Sparrow
{
    public static unsafe class UnmanagedMemory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr Copy(byte* dest, byte* src, long count)
        {
            Debug.Assert(count >= 0);
            return Platform.RunningOnPosix
                ? PosixUnmanagedMemory.Copy(dest, src, count)
                : Win32UnmanagedMemory.Copy(dest, src, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Compare(byte* b1, byte* b2, long count)
        {
            Debug.Assert(count >= 0);
            return Platform.RunningOnPosix
                ? PosixUnmanagedMemory.Compare(b1, b2, count)
                : Win32UnmanagedMemory.Compare(b1, b2, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Move(byte* b1, byte* b2, long count)
        {
            Debug.Assert(count >= 0);
            return Platform.RunningOnPosix
                ? PosixUnmanagedMemory.Move(b1, b2, count)
                : Win32UnmanagedMemory.Move(b1, b2, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr Set(byte* dest, int c, long count)
        {
            Debug.Assert(count >= 0);
            return Platform.RunningOnPosix
                ? PosixUnmanagedMemory.Set(dest, c, count)
                : Win32UnmanagedMemory.Set(dest, c, count);
        }
    }
}
