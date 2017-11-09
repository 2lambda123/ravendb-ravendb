﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using Raven.Client;
using Raven.Client.Documents.Smuggler;
using Raven.Client.Util;
using Raven.Server.Documents;
using Raven.Server.ServerWide.Context;
using Raven.Server.Smuggler.Documents.Data;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Smuggler.Documents
{
    public class CsvStreamSource : ISmugglerSource, IDisposable
    {
        private Stream _stream;
        private DocumentsOperationContext _context;
        private DatabaseItemType _currentType;
        private string _collection;
        private StreamReader _reader;
        private CsvReader _csvReader;
        private bool _hasId;
        private int _idIndex;
        private bool _hasCollection;
        private int _collectionIndex;
        /// <summary>
        /// This dictionary maps the index of a property to its nested segments.
        /// </summary>
        private Dictionary<int, string[]> _nestedPropertyDictionary = null;

        public CsvStreamSource(Stream stream, DocumentsOperationContext context, string collection)
        {
            _stream = stream;
            _context = context;
            _currentType = DatabaseItemType.Documents;
            _collection = collection;
        }

        public IDisposable Initialize(DatabaseSmugglerOptions options, SmugglerResult result, out long buildVersion)
        {
            buildVersion = 40;
            _reader = new StreamReader(_stream);
            _csvReader = new CsvReader(_reader);
            _csvReader.Configuration.Delimiter = ",";
            return new DisposableAction(() =>
            {
                _reader.Dispose();
                _csvReader.Dispose();
            });
        }

        private bool _headersProcessed;
        private readonly List<IDisposable> _disposibales = new List<IDisposable>();
        private static readonly string CollectionFullPath = $"{Constants.Documents.Metadata.Key}.{Constants.Documents.Metadata.Collection}";

        private void ProcessFieldsIfNeeded()
        {
            if (_headersProcessed)
                return;
            for (var i = 0; i < _csvReader.FieldHeaders.Length; i++)
            {
                if (_csvReader.FieldHeaders[i].Equals(Constants.Documents.Metadata.Id))
                {
                    _hasId = true;
                    _idIndex = i;
                }
                if (_csvReader.FieldHeaders[i].Equals(Constants.Documents.Metadata.Collection) || _csvReader.FieldHeaders[i].Equals(CollectionFullPath))
                {
                    _hasCollection = true;
                    _collectionIndex = i;
                }
                if (_csvReader.FieldHeaders[i][0] == '@')
                    continue;
                var indexOfDot = _csvReader.FieldHeaders[i].IndexOf('.');
                //We probably have a nested property
                if (indexOfDot >= 0)
                {
                    if (_nestedPropertyDictionary == null)
                        _nestedPropertyDictionary = new Dictionary<int, string[]>();
                    (var arr,var hasSegments) = SplitByDotWhileIgnoringEscapedDot(_csvReader.FieldHeaders[i]);
                    //May be false if all dots are escaped
                    if (hasSegments)
                        _nestedPropertyDictionary[i] = arr;
                }
            }
            _headersProcessed = true;
        }

        private (string[] Segments, bool HasSegments) SplitByDotWhileIgnoringEscapedDot(string csvReaderFieldHeader)
        {            
            List<string> segments = new List<string>();
            bool escaped = false;
            int startSegment = 0;
            //Need to handle the general case where we can have Foo.'Foos.Name'.First
            for (var i = 0; i < csvReaderFieldHeader.Length; i++)
            {
                if (csvReaderFieldHeader[i] == '.' && escaped == false)
                {
                    segments.Add(csvReaderFieldHeader.Substring(startSegment, i - startSegment));
                    startSegment = i + 1;
                    continue;
                }
                if (csvReaderFieldHeader[i] == '\'')
                {
                    escaped ^= escaped;
                }
            }
            //No segments, this means the dot is escaped e.g. 'Foo.Bar'
            if (startSegment == 0)
            {
                return (null, false);
            }
            //Adding the last segment
            segments.Add(csvReaderFieldHeader.Substring(startSegment, csvReaderFieldHeader.Length - startSegment));
            //At this point we have atleast 2 segments
            return (segments.ToArray(), true);
        }

        public DatabaseItemType GetNextType()
        {
            var type = _currentType;
            _currentType = DatabaseItemType.None;
            return type;
        }

        public IEnumerable<DocumentItem> GetDocuments(List<string> collectionsToExport, INewDocumentActions actions)
        {
            while (_csvReader.Read())
            {
                ProcessFieldsIfNeeded();
                var context = actions.GetContextForNewDocument();
                yield return ConvertRecordToDocumentItem(context, _csvReader.CurrentRecord, _csvReader.FieldHeaders, _collection);
            }

        }

        private DocumentItem ConvertRecordToDocumentItem(DocumentsOperationContext context, string[] csvReaderCurrentRecord, string[] csvReaderFieldHeaders, string collection)
        {
            try
            {
                var idStr = _hasId ? csvReaderCurrentRecord[_idIndex] : _hasCollection ? $"{csvReaderCurrentRecord[_collectionIndex]}/" : $"{collection}/";
                var data = new DynamicJsonValue();
                for (int i = 0; i < csvReaderFieldHeaders.Length; i++)
                {
                    //ignoring reserved properties
                    if (csvReaderFieldHeaders[i][0] == '@')
                    {
                        if (_hasCollection && i == _collectionIndex)
                        {
                            SetCollectionForDocument(csvReaderCurrentRecord[_collectionIndex], data);
                        }
                        continue;
                    }

                    if (_nestedPropertyDictionary != null && _nestedPropertyDictionary.TryGetValue(i, out var segments))
                    {
                        var nestedData = data;
                        for (var j = 0;; j++)
                        {
                            //last segment holds the data
                            if (j == segments.Length - 1)
                            {
                                nestedData[segments[j]] = ParseValue(csvReaderCurrentRecord[i]);
                                break; //we are done
                            }
                            //Creating the objects along the path if needed e.g. Foo.Bar.Name will create the 'Bar' oject if needed
                            if (nestedData[segments[j]] == null)
                            {
                                var tmpRef = new DynamicJsonValue();
                                nestedData[segments[j]] = tmpRef;
                                nestedData = tmpRef;
                            }
                        }
                        continue;
                    }
                    data[csvReaderFieldHeaders[i]] = ParseValue(csvReaderCurrentRecord[i]);
                }

                if (_hasCollection == false)
                {
                    SetCollectionForDocument(collection, data);
                }

                return new DocumentItem
                {
                    Document = new Document
                    {
                        Data = context.ReadObject(data, idStr),
                        Id = context.GetLazyString(idStr),
                        ChangeVector = string.Empty,
                        Flags = DocumentFlags.None,
                        NonPersistentFlags = NonPersistentDocumentFlags.FromSmuggler
                    },
                    Attachments = null
                };
            }
            finally
            {
                foreach (var disposeMe in _disposibales)
                {
                    disposeMe.Dispose();
                }
                _disposibales.Clear();
            }
        }

        private void SetCollectionForDocument(string collection, DynamicJsonValue data)
        {
            var metadata = new DynamicJsonValue
            {
                [Constants.Documents.Metadata.Collection] = collection
            };
            data[Constants.Documents.Metadata.Key] = metadata;
        }

        private object ParseValue(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            if (s.StartsWith('\"') && s.EndsWith('\"'))
                return s.Substring(1, s.Length - 2);
            if (s.StartsWith('[') && s.EndsWith(']'))
            {
                var array = _context.ParseBufferToArray(s,"CsvImport/ArrayValue",
                    BlittableJsonDocumentBuilder.UsageMode.None);
                _disposibales.Add(array);
                return array;
            }
            if (char.IsDigit(s[0]) || s[0] == '-')
            {
                if (s.IndexOf('.') > 0)
                {
                    if (decimal.TryParse(s, out var dec))
                        return dec;
                }
                else
                {
                    if (long.TryParse(s, out var l))
                        return l;
                }
            }

            if ((s.Length == 4 && s[0] == 't' || s.Length == 5 && s[0] == 'f') && bool.TryParse(s, out var b))
            {
                return b;
            }

            if (s.Length == 4 && s.Equals("null"))
                return null;

            return s;
        }

        public IEnumerable<DocumentItem> GetRevisionDocuments(List<string> collectionsToExport, INewDocumentActions actions)
        {
            return Enumerable.Empty<DocumentItem>();
        }

        public IEnumerable<DocumentItem> GetLegacyAttachments(INewDocumentActions actions)
        {
            return Enumerable.Empty<DocumentItem>();
        }

        public IEnumerable<DocumentTombstone> GetTombstones(List<string> collectionsToExport, INewDocumentActions actions)
        {
            return Enumerable.Empty<DocumentTombstone>();
        }

        public IEnumerable<DocumentConflict> GetConflicts(List<string> collectionsToExport, INewDocumentActions actions)
        {
            yield break;
        }

        public IEnumerable<IndexDefinitionAndType> GetIndexes()
        {
            return Enumerable.Empty<IndexDefinitionAndType>();
        }

        public IDisposable GetIdentities(out IEnumerable<(string Prefix, long Value)> identities)
        {
            identities = Enumerable.Empty<(string Prefix, long Value)>();
            return new DisposableAction(() => { });
        }

        public long SkipType(DatabaseItemType type)
        {
            return 0;
        }

        public void Dispose()
        {
        }
    }
}
