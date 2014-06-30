﻿using Raven.Abstractions.Data;
using Raven.Client.Util;
using Raven.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace Raven.Client.FileSystem.Impl
{

    internal class UploadFileOperation : IFilesOperation
    {
        public string Path { get; private set; }
        public RavenJObject Metadata { get; private set; }
        public Etag Etag { get; private set; }

        
        public long Size { get; private set; }
        public Action<Stream> StreamWriter { get; private set; }


        public UploadFileOperation(string path, long size, Action<Stream> stream, RavenJObject metadata = null, Etag etag = null)
        {
            this.Path = path;
            this.Metadata = metadata;
            this.Etag = etag;

            this.StreamWriter = stream;
            this.Size = size;
        }

        public async Task Execute(IAsyncFilesSession session)
        {
            var commands = session.Commands;

            var pipe = new BlockingStream(10);           
            var task = Task.Run(() => StreamWriter(pipe))
                           .ContinueWith(x => { pipe.CompleteWriting(); })
                           .ConfigureAwait(false);

            await commands.UploadAsync(Path, pipe, Metadata, Size, null)
                          .ConfigureAwait(false);
        }
    }
}
