﻿using System;
using System.IO;
using Lucene.Net.Store;
using Raven.Server.Utils;
using Voron.Impl;
using Voron;

namespace Raven.Server.Indexing
{
    public unsafe class VoronIndexOutput : BufferedIndexOutput
    {
        public static readonly int MaxFileChunkSize = 128 * 1024 * 1024;

        private readonly string _name;
        private readonly Transaction _tx;
        private readonly FileStream _file;
        private LuceneFileInfo _fileInfo;

        public VoronIndexOutput(string tempPath, string name, Transaction tx, LuceneFileInfo fileInfo)
        {
            _name = name;
            _tx = tx;
            _fileInfo = fileInfo;
            var fileTempPath = Path.Combine(tempPath, name + "_" + Guid.NewGuid());
            //TODO: Pass this flag
            //const FileOptions FILE_ATTRIBUTE_TEMPORARY = (FileOptions)256;
            _file = new FileStream(fileTempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite,
                4096, FileOptions.DeleteOnClose);
        }

        public override void FlushBuffer(byte[] b, int offset, int len)
        {
            _file.Write(b, offset, len);
        }

        /// <summary>Random-access methods </summary>
        public override void Seek(long pos)
        {
            base.Seek(pos);
            _file.Seek(pos, SeekOrigin.Begin);
        }

        public override long Length => _file.Length;

        public override void SetLength(long length)
        {
            _file.SetLength(length);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            var tree = _tx.CreateTree(_name);
            _file.Seek(0, SeekOrigin.Begin);

            var size = _file.Length;

            var numberOfChunks = size / MaxFileChunkSize + (size % MaxFileChunkSize != 0 ? 1 : 0);

            Slice key;
            for (int i = 0; i < numberOfChunks; i++)
            {
                using (Slice.From(_tx.Allocator, i.ToString("D9"), out key))
                {
                    tree.Add(key, new LimitedStream(_file, _file.Position, Math.Min(_file.Position + MaxFileChunkSize, _file.Length)));
                }
            }

            var files = _tx.ReadTree("Files");
            using (Slice.From(_tx.Allocator, _name, out key))
            {
                var pos = files.DirectAdd(key, sizeof(LuceneFileInfo));

                _fileInfo.Length = size;
                _fileInfo.Version++;

                *(LuceneFileInfo*)pos = _fileInfo;
            }

            _file.Dispose();
        }
    }
}