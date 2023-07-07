﻿using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Attachments;
using Sparrow.Json;

namespace Raven.Server.Documents.Handlers.Processors.Attachments
{
    internal abstract class AbstractAttachmentHandlerProcessorForGetAttachment<TRequestHandler, TOperationContext> : AbstractDatabaseHandlerProcessor<TRequestHandler, TOperationContext>
        where TOperationContext : JsonOperationContext 
        where TRequestHandler : AbstractDatabaseRequestHandler<TOperationContext>
    {
        private readonly bool _isDocument;

        protected AbstractAttachmentHandlerProcessorForGetAttachment([NotNull] TRequestHandler requestHandler, bool isDocument) : base(requestHandler)
        {
            _isDocument = isDocument;
        }

        protected abstract ValueTask GetAttachmentAsync(TOperationContext context, string documentId, string name, AttachmentType type, string changeVector, CancellationToken token);

        public override async ValueTask ExecuteAsync()
        {
            var documentId = RequestHandler.GetQueryStringValueAndAssertIfSingleAndNotEmpty("id");
            var name = RequestHandler.GetQueryStringValueAndAssertIfSingleAndNotEmpty("name");

            using (ContextPool.AllocateOperationContext(out TOperationContext context))
            using (var token = RequestHandler.CreateHttpRequestBoundOperationToken())
            {
                var type = AttachmentType.Document;
                string changeVector = null;
                if (_isDocument == false)
                {
                    var stream = RequestHandler.TryGetRequestFromStream("ChangeVectorAndType") ?? RequestHandler.RequestBodyStream();
                    var request = await context.ReadForDiskAsync(stream, "GetAttachment");

                    if (request.TryGet("Type", out string typeString) == false ||
                        Enum.TryParse(typeString, out type) == false)
                        throw new ArgumentException("The 'Type' field in the body request is mandatory");

                    if (request.TryGet("ChangeVector", out changeVector) == false && changeVector != null)
                        throw new ArgumentException("The 'ChangeVector' field in the body request is mandatory");
                }
                
                await GetAttachmentAsync(context, documentId, name, type, changeVector, token.Token);
            }
        }
    }
}
