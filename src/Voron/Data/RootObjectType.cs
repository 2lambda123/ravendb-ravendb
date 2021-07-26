﻿namespace Voron.Data
{
    public enum RootObjectType : byte
    {
        None = 0,
        VariableSizeTree = 1,
        EmbeddedFixedSizeTree = 2,
        FixedSizeTree = 3,
        ObseleteValue = 4, // used to be PrefixTree, never used in prod 
        Table = 5,
        CompactTree
    }
}
