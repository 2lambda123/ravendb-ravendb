﻿using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Raven.Server.Extensions;
using Raven.Server.ServerWide;
using Sparrow.Json;

namespace Raven.Server.Utils
{
    public class AsyncBlittableJsonTextWriterForDebug : AsyncBlittableJsonTextWriter
    {
        private readonly ServerStore _serverStore;
        private bool _isFirst = true;
        private bool _isOnlyWrite;
        private bool _isFromClientApi;
        private bool _isFromStudio;

        public AsyncBlittableJsonTextWriterForDebug(HttpRequest request, JsonOperationContext context, ServerStore serverStore, Stream stream) : base(context, stream)
        {
            _serverStore = serverStore;
            _isFromClientApi = request.IsFromClientApi();
            _isFromStudio = request.IsFromStudio();
        }
        
        public override void WriteStartObject()
        {
            base.WriteStartObject();

            if (_isFirst && !_isFromStudio && !_isFromClientApi)
            {
                _isFirst = false;

                WritePropertyName("@Metadata");
                WriteStartObject();
                WritePropertyName(nameof(DateTime));
                WriteDateTime(DateTime.UtcNow, true);
                WriteComma();
                WritePropertyName(nameof(_serverStore.Server.WebUrl));
                WriteString(_serverStore.Server.WebUrl);
                WriteComma();
                WritePropertyName(nameof(_serverStore.NodeTag));
                WriteString(_serverStore.NodeTag);

                WriteEndObject();

                _isOnlyWrite = true;
            }
        }

        protected override void EnsureBuffer(int len)
        {
            base.EnsureBuffer(len);

            if (_isOnlyWrite && !_isFromStudio && !_isFromClientApi)
            {
                _isOnlyWrite = false;
                WriteComma();
            }
        }

        public override void WriteEndObject()
        {
            _isOnlyWrite = false;
            base.WriteEndObject();
        }
    }
}
