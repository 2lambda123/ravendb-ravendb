﻿using System;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
using BenchmarkDotNet.Running;

namespace Micro.Benchmark
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine($"{nameof(Sse)} support: {Sse.IsSupported}");
            Console.WriteLine($"{nameof(Sse2)} support: {Sse2.IsSupported}");
            Console.WriteLine($"{nameof(Sse3)} support: {Sse3.IsSupported}");
            Console.WriteLine($"{nameof(Sse41)} support: {Sse41.IsSupported}");

            Console.WriteLine($"{nameof(Avx)} support: {Avx.IsSupported}");
            Console.WriteLine($"{nameof(Avx2)} support: {Avx2.IsSupported}");

            ////var p = new Allocators();
            ////p.Allocate_Native_SameSize();
            ////for (int i = 0; i < 100000; i++)
            ////    p.Allocate_Pool_SameSize();

            BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
        }
    }
}
