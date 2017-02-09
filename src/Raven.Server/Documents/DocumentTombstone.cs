﻿using System;
using Raven.Client.Replication.Messages;
using Sparrow.Json;

namespace Raven.Server.Documents
{
    public class DocumentTombstone
    {
        public LazyStringValue Key;

        public DocumentFlags Flags;

        public LazyStringValue LoweredKey;

        public long DeletedEtag;

        public long Etag;

        public long StorageId;

        public LazyStringValue Collection;

        public ChangeVectorEntry[] ChangeVector;

        public short TransactionMarker;

        public DateTime LastModified;
    }
}