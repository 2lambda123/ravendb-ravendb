﻿using Raven.Server.Documents;
using Raven.Server.Documents.Revisions;
using Raven.Server.Json;
using Raven.Server.ServerWide.Context;
using Sparrow.Binary;
using Voron.Data.Tables;
using static Raven.Server.Documents.DocumentsStorage;
using static Raven.Server.Documents.Revisions.RevisionsStorage;

namespace Raven.Server.Storage.Schema.Updates.Documents
{
    public unsafe class From14 : ISchemaUpdate
    {
        public bool Update(UpdateStep step)
        {
            step.DocumentsStorage.RevisionsStorage = new RevisionsStorage(step.DocumentsStorage.DocumentDatabase, step.WriteTx);

            // update revisions
            using (step.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            {
                foreach (var collection in step.DocumentsStorage.RevisionsStorage.GetCollections(step.ReadTx))
                {
                    var collectionName = new CollectionName(collection);
                    var tableName = collectionName.GetTableName(CollectionTableType.Revisions);
                    var readTable = step.ReadTx.OpenTable(RevisionsSchema, tableName);
                    if (readTable == null)
                        continue;

                    var writeTable = step.DocumentsStorage.RevisionsStorage.EnsureRevisionTableCreated(step.WriteTx, collectionName);
                    foreach (var read in readTable.SeekForwardFrom(RevisionsSchema.FixedSizeIndexes[CollectionRevisionsEtagsSlice], 0, 0))
                    {
                        using (TableValueReaderUtil.CloneTableValueReader(context, read))
                        using (writeTable.Allocate(out TableValueBuilder write))
                        {
                            var flags = TableValueToFlags((int)Columns.Flags, ref read.Reader);
                            var lastModified = TableValueToDateTime((int)Columns.LastModified, ref read.Reader);

                            write.Add(read.Reader.Read((int)Columns.ChangeVector, out int size), size);
                            write.Add(read.Reader.Read((int)Columns.LowerId, out size), size);
                            write.Add(read.Reader.Read((int)Columns.RecordSeparator, out size), size);
                            write.Add(read.Reader.Read((int)Columns.Etag, out size), size);
                            write.Add(read.Reader.Read((int)Columns.Id, out size), size);
                            write.Add(read.Reader.Read((int)Columns.Document, out size), size);
                            write.Add((int)flags);
                            write.Add(read.Reader.Read((int)Columns.DeletedEtag, out size), size);
                            write.Add(lastModified.Ticks);
                            write.Add(read.Reader.Read((int)Columns.TransactionMarker, out size), size);
                            if ((flags & DocumentFlags.Resolved) == DocumentFlags.Resolved)
                            {
                                write.Add((int)DocumentFlags.Resolved);
                            }
                            else
                            {
                                write.Add(0);
                            }
                            write.Add(Bits.SwapBytes(lastModified.Ticks));
                            writeTable.Set(write, true);
                        }
                    }
                }
            }

            return true;
        }
    }
}
