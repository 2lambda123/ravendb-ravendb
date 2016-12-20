﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Lucene.Net.Support;
using Raven.Client.Replication.Messages;
using Raven.Server.Documents;
using Raven.Server.Extensions;
using Raven.Server.ServerWide.Context;
using Sparrow;
using Sparrow.Binary;
using Sparrow.Json.Parsing;
using Voron;
using Voron.Data.BTrees;
using Voron.Data.Tables;

namespace Raven.Server.Utils
{
    public static unsafe class ReplicationUtils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ChangeVectorToString(Dictionary<Guid, long> changeVector)
        {
            var sb = new StringBuilder();
            foreach (var kvp in changeVector)
                sb.Append($"{kvp.Key}:{kvp.Value};");

            return sb.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ChangeVectorToString(ChangeVectorEntry[] changeVector)
        {
            var sb = new StringBuilder();
            foreach (var kvp in changeVector)
                sb.Append($"{kvp.DbId}:{kvp.Etag};");

            return sb.ToString();
        }


        public static void WriteChangeVectorTo(DocumentsOperationContext context, Dictionary<Guid, long> changeVector, Tree tree)
        {
            Guid dbId;
            long etagBigEndian;
            Slice keySlice;
            Slice valSlice;
            using (Slice.External(context.Allocator, (byte*)&dbId, sizeof(Guid), out keySlice))
            using (Slice.External(context.Allocator, (byte*)&etagBigEndian, sizeof(long), out valSlice))
            {
                foreach (var kvp in changeVector)
                {
                    dbId = kvp.Key;
                    etagBigEndian = Bits.SwapBytes(kvp.Value);
                    tree.Add(keySlice, valSlice);
                }
            }
        }

        public static void WriteChangeVectorTo(ByteStringContext context, Dictionary<Guid, long> changeVector, Tree tree)
        {
            Guid dbId;
            long etagBigEndian;
            Slice keySlice;
            Slice valSlice;
            using (Slice.External(context, (byte*)&dbId, sizeof(Guid), out keySlice))
            using (Slice.External(context, (byte*)&etagBigEndian, sizeof(long), out valSlice))
            {
                foreach (var kvp in changeVector)
                {
                    dbId = kvp.Key;
                    etagBigEndian = Bits.SwapBytes(kvp.Value);
                    tree.Add(keySlice, valSlice);
                }
            }           
        }

        public static TEnum GetEnumFromTableValueReader<TEnum>(TableValueReader tvr, int index)
        {
            int size;
            var storageTypeNum = *(int*)tvr.Read(index, out size);
            return (TEnum)Enum.ToObject(typeof(TEnum), storageTypeNum);
        }

        public static ChangeVectorEntry[] GetChangeVectorEntriesFromTableValueReader(TableValueReader tvr, int index)
        {
            int size;
            var pChangeVector = (ChangeVectorEntry*)tvr.Read(index, out size);
            var changeVector = new ChangeVectorEntry[size / sizeof(ChangeVectorEntry)];
            for (int i = 0; i < changeVector.Length; i++)
            {
                changeVector[i] = pChangeVector[i];
            }
            return changeVector;
        }

        public static ChangeVectorEntry[] ReadChangeVectorFrom(Tree tree)
        {
            var changeVector = new ChangeVectorEntry[tree.State.NumberOfEntries];
            using (var iter = tree.Iterate(false))
            {
                if (iter.Seek(Slices.BeforeAllKeys) == false)
                    return changeVector;
                var buffer = new byte[sizeof(Guid)];
                int index = 0;
                do
                {
                    var read = iter.CurrentKey.CreateReader().Read(buffer, 0, sizeof(Guid));
                    if (read != sizeof(Guid))
                        throw new InvalidDataException($"Expected guid, but got {read} bytes back for change vector");

                    changeVector[index].DbId = new Guid(buffer);
                    changeVector[index].Etag = iter.CreateReaderForCurrent().ReadBigEndianInt64();
                    index++;
                } while (iter.MoveNext());
            }
            return changeVector;
        }

        public static ChangeVectorEntry[] GetChangeVectorForWrite(ChangeVectorEntry[] existingChangeVector, Guid dbid, long etag)
        {
            if (existingChangeVector == null || existingChangeVector.Length == 0)
            {
                return new[]
                {
                    new ChangeVectorEntry
                    {
                        DbId = dbid,
                        Etag = etag
                    }
                };
            }

            return UpdateChangeVectorWithNewEtag(dbid, etag, existingChangeVector);
        }

        public static ChangeVectorEntry[] UpdateChangeVectorWithNewEtag(Guid dbId, long newEtag, ChangeVectorEntry[] changeVector)
        {
            var length = changeVector.Length;
            for (int i = 0; i < length; i++)
            {
                if (changeVector[i].DbId == dbId)
                {
                    changeVector[i].Etag = newEtag;
                    return changeVector;
                }
            }
            Array.Resize(ref changeVector, length + 1);
            changeVector[length].DbId = dbId;
            changeVector[length].Etag = newEtag;
            return changeVector;
        }

        public static ChangeVectorEntry[] MergeVectors(ChangeVectorEntry[] vectorA, ChangeVectorEntry[] vectorB)
        {
            var merged = new ChangeVectorEntry[Math.Max(vectorA.Length, vectorB.Length)];
            var inx = 0;
            foreach (var entryA in vectorA)
            {
                var etagA = entryA.Etag;
                ChangeVectorEntry first = new ChangeVectorEntry();
                foreach (var e in vectorB)
                {
                    if (e.DbId == entryA.DbId)
                    {
                        first = e;
                        break;
                    }
                }
                var etagB = first.Etag;

                merged[inx++] = new ChangeVectorEntry
                {
                    DbId = entryA.DbId,
                    Etag = Math.Max(etagA,etagB)
                };
            }
            return merged;
        }

        public static ChangeVectorEntry[] MergeVectors(IReadOnlyList<ChangeVectorEntry[]> changeVectors)
        {
            var mergedVector = new Dictionary<Guid,long>();

            foreach (var vector in changeVectors)
            {
                foreach (var entry in vector)
                {
                    if (mergedVector.ContainsKey(entry.DbId))
                        continue;

                    long maxEtag = 0;
                    var hasFoundAny = false;
                    foreach (var searchVector in changeVectors)
                    {
                        if(searchVector == vector)
                            continue;
                        long etag;
                        if (searchVector.TryFindEtagByDbId(entry.DbId, out etag) && etag > maxEtag)
                        {
                            hasFoundAny = true;
                            maxEtag = etag;
                        }
                    }

                    if(hasFoundAny)
                        mergedVector.Add(entry.DbId, maxEtag);
                }
            }

            return mergedVector.Select(kvp => new ChangeVectorEntry
                                        {
                                            DbId = kvp.Key,
                                            Etag = kvp.Value
                                        }).ToArray();
        }

        private static bool TryFindEtagByDbId(this ChangeVectorEntry[] changeVector, Guid dbId, out long etag)
        {
            etag = 0;
            for (int i = 0; i < changeVector.Length; i++)
            {
                if (changeVector[i].DbId == dbId)
                {
                    etag = changeVector[i].Etag;
                    return true;
                }
            }

            return false;
        }

        public static DynamicJsonValue GetJsonForConflicts(string docId, IEnumerable<DocumentConflict> conflicts)
        {
            var conflictsArray = new DynamicJsonArray();
            foreach (var c in conflicts)
            {
                conflictsArray.Add(new DynamicJsonValue
                {
                    ["ChangeVector"] = c.ChangeVector.ToJson(),
                });
            }

            return new DynamicJsonValue
            {
                ["Message"] = "Conflict detected on " + docId + ", conflict must be resolved before the document will be accessible",
                ["DocId"] = docId,
                ["Conflics"] = conflictsArray
            };
        }

    }
}
