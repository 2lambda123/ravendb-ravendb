using System;
using System.Collections.Generic;
using System.Linq;
using Sparrow;
using Sparrow.Server;
using Voron;
using Voron.Data.BTrees;
using Voron.Data.CompactTrees;
using Voron.Data.Containers;
using Voron.Data.Fixed;
using Voron.Impl;

namespace Corax;

public unsafe struct TermsReader : IDisposable
{
    private readonly LowLevelTransaction _llt;
    private readonly FixedSizeTree _fst;
    private readonly CompactKeyCacheScope _xKeyScope, _yKeyScope;

    private const int CacheSize = 64;
    private readonly (long Key, UnmanagedSpan Term)* _cache;
    private ByteStringContext<ByteStringMemoryCache>.InternalScope _cacheScope;
    private Page _lastPage;

    public TermsReader(LowLevelTransaction llt, Tree entriesToTermsTree, Slice name)
    {
        _llt = llt;
        _cacheScope = _llt.Allocator.Allocate(sizeof((long, UnmanagedSpan)) * CacheSize, out var bs);
        bs.Clear();
        _lastPage = new();
        _cache = ((long, UnmanagedSpan)*)bs.Ptr;
        _fst = entriesToTermsTree.FixedTreeFor(name, sizeof(long));
        _xKeyScope = new CompactKeyCacheScope(_llt);
        _yKeyScope = new CompactKeyCacheScope(_llt);
    }

    public string GetTermFor(long id)
    {
        TryGetTermFor(id, out string s);
        return s;
    }
    
    public bool TryGetTermFor(long id, out string term)
    {
        using var _ = _fst.Read(id, out var termId);
        if (termId.HasValue == false)
        {
            term = null;
            return false;
        }

        long termContainerId = termId.ReadInt64();
        var item = Container.Get(_llt, termContainerId);
        int remainderBits = item.Address[0] >> 4;
        int encodedKeyLengthInBits = (item.Length - 1) * 8 - remainderBits;

        _xKeyScope.Key.Set(encodedKeyLengthInBits, item.ToSpan()[1..], item.PageLevelMetadata);
        term = _xKeyScope.Key.ToString();
        return true;
    }

    public void GetDecodedTerms(long x, out ReadOnlySpan<byte> xTerm, long y, out ReadOnlySpan<byte> yTerm)
    {
        // we have to do this so we won't get both terms from the same scope, maybe overwriting one another 
        ReadTerm(x, out xTerm, _xKeyScope);
        ReadTerm(y, out yTerm, _yKeyScope);
    }
    
    private void ReadTerm(long id, out ReadOnlySpan<byte> term, CompactKeyCacheScope scope)
    {
        using var _ = _fst.Read(id, out var termId);
        if (termId.HasValue)
        {
            long termContainerId = termId.ReadInt64();
            var item = Container.Get(_llt, termContainerId);
            int remainderBits = item.Address[0] >> 4;
            int encodedKeyLengthInBits = (item.Length - 1) * 8 - remainderBits;

            scope.Key.Set(encodedKeyLengthInBits, item.ToSpan()[1..], item.PageLevelMetadata);
            term = scope.Key.Decoded();
        }
        else
        {
            term = ReadOnlySpan<byte>.Empty;
        }
    }

    private UnmanagedSpan GetTerm(long entryId)
    {
        var idx = (uint)Hashing.Mix(entryId) % CacheSize;
        ref (long Key, UnmanagedSpan Value) cache = ref _cache[idx];

        if (cache.Key == entryId)
        {
            return cache.Value;
        }

        using var _ = _fst.Read(entryId, out var s);
        UnmanagedSpan term = UnmanagedSpan.Empty;
        if (s.HasValue)
        {
            long termId = s.ReadInt64();
            var item = Container.MaybeGetFromSamePage(_llt, ref _lastPage, termId);
            term = item.ToUnmanagedSpan();
        }

        cache = (entryId, term);
        return term;
    }

    public (long, UnmanagedSpan)[] CacheView => new Span<(long, UnmanagedSpan)>(_cache, CacheSize)
        .ToArray().Where(x =>x.Item1 != 0).ToArray();

    public int Compare(long x, long y)
    {
        var xItem = GetTerm(x);
        var yItem = GetTerm(y);
        
        var match = AdvMemory.Compare(xItem.Address + 1, yItem.Address + 1, Math.Min(xItem.Length - 1, yItem.Length - 1));
        if (match != 0)
            return match;
        var xItemLengthInBits = (xItem.Length - 1) * 8 - (xItem.Address[0] >> 4);
        var yItemLengthInBits = (yItem.Length - 1) * 8 - (yItem.Address[0] >> 4);
        return xItemLengthInBits - yItemLengthInBits;
    }

    public void Dispose()
    {
        _cacheScope.Dispose();
        _yKeyScope.Dispose();
        _xKeyScope .Dispose();
        _fst.Dispose();
    }
}
