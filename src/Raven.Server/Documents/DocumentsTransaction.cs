﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Raven.Abstractions.Data;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Voron.Impl;

namespace Raven.Server.Documents
{
    public class DocumentsTransaction : RavenTransaction
    {
        private readonly DocumentsOperationContext _context;

        private readonly DocumentsNotifications _notifications;

        private readonly List<DocumentChangeNotification> _afterCommitNotifications = new List<DocumentChangeNotification>();

        public DocumentsTransaction(DocumentsOperationContext context, Transaction transaction, DocumentsNotifications notifications)
            : base(transaction)
        {
            _context = context;
            _notifications = notifications;
        }

        public void AddAfterCommitNotification(DocumentChangeNotification notification)
        {
            _afterCommitNotifications.Add(notification);
        }

        public override void Dispose()
        {
            if (_context.Transaction != this)
                throw new InvalidOperationException("There is a different transaction in context.");


            _context.Transaction = null;
            base.Dispose();

            if (InnerTransaction.LowLevelTransaction.Committed)
                AfterCommit();
        }

        private void AfterCommit()
        {
            if (ThreadPool.QueueUserWorkItem(state => ((DocumentsTransaction)state).RaiseNotifications(), this) == false)
            {
                RaiseNotifications(); // raise immediately
            }
        }

        private void RaiseNotifications()
        {
            foreach (var notification in _afterCommitNotifications)
            {
                if (notification.IsSystemDocument)
                {
                    _notifications.RaiseSystemNotifications(notification);
                }
                else
                {
                    _notifications.RaiseNotifications(notification);
                }
            }
        }
    }
}