﻿using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Raven.Client.Json
{
    internal class BlittableJsonContent : HttpContent
    {
        private readonly object _locker;
        private readonly Action<Stream> _writer;

        public BlittableJsonContent(object locker, Action<Stream> writer)
        {
            _locker = locker;
            _writer = writer;
            Headers.ContentEncoding.Add("gzip");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            lock (_locker)
            {
                using (var gzipStream = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true))
                {
                    _writer(gzipStream);
                }
            }
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
