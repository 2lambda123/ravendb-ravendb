﻿using System;
using System.Net;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Raven.Abstractions.Data;
using Raven.Abstractions.Indexing;
using Raven.Database.Util;
using Raven.Studio.Commands;
using Raven.Studio.Extensions;
using Raven.Studio.Features.Documents;
using Raven.Studio.Infrastructure;
using Raven.Abstractions.Extensions;

namespace Raven.Studio.Models
{
    public class ConflictsModel : PageViewModel
    {
        private static readonly string ConflictsIndexName = "Raven/ConflictDocuments";
        private IDisposable changesSubscription;
        private ICommand deleteSelectedDocuments;
        private ICommand copyIdsToClipboard;
        private ICommand editDocument;
        private static ConcurrentSet<string> performedIndexChecks = new ConcurrentSet<string>();

        public VirtualCollection<ViewableDocument> ConflictDocuments { get; private set; }

        public ItemSelection<VirtualItem<ViewableDocument>> ItemSelection { get; private set; }

        public ConflictsModel()
        {
            ConflictDocuments = new VirtualCollection<ViewableDocument>(new ConflictDocumentsCollectionSource(), 30, 30);
            ItemSelection = new ItemSelection<VirtualItem<ViewableDocument>>();
        }

        public ICommand DeleteSelectedDocuments
        {
            get { return deleteSelectedDocuments ?? (deleteSelectedDocuments = new DeleteDocumentsCommand(ItemSelection)); }
        }

        public ICommand CopyIdsToClipboard
        {
            get { return copyIdsToClipboard ?? (copyIdsToClipboard = new CopyDocumentsIdsCommand(ItemSelection)); }
        }

        public ICommand EditDocument
        {
            get
            {
                return editDocument ??
                       (editDocument =
                        new EditVirtualDocumentCommand() {  });
            }
        }

        protected override void OnViewLoaded()
        {
            ApplicationModel.Database
                .ObservePropertyChanged()
                .TakeUntil(Unloaded)
                .Subscribe(_ =>
                {
                    EnsureIndexExists();
                    ObserveSourceChanges();
                    ConflictDocuments.Refresh(RefreshMode.ClearStaleData);
                });

            ObserveSourceChanges();
            ConflictDocuments.Refresh(RefreshMode.ClearStaleData);

            EnsureIndexExists();
        }

        private void EnsureIndexExists()
        {
            if (performedIndexChecks.Contains(ApplicationModel.Database.Value.Name))
            {
                return;
            }

            if (!performedIndexChecks.TryAdd(ApplicationModel.Database.Value.Name))
            {
                return;
            }

            ApplicationModel.DatabaseCommands.QueryAsync(ConflictsIndexName, new IndexQuery() {PageSize = 0},
                                                         new string[0])
                            .ContinueWith(task =>
                            {
                                if (task.IsFaulted)
                                {
                                    var exception = task.Exception.ExtractSingleInnerException() as WebException;
                                    if (exception != null && (exception.Response as HttpWebResponse).StatusCode == HttpStatusCode.NotFound)
                                    {
                                        CreateIndex();
                                    }
                                }
                            });
        }

        private void CreateIndex()
        {
            var index = new IndexDefinition()
            {
                Map = @"from doc in docs
                        let id = doc[""@metadata""][""@id""]
                        where doc[""@metadata""][""Raven-Replication-Conflict""] == true && (id.Length < 47 || !id.Substring(id.Length - 47).StartsWith(""/conflicts/"", StringComparison.OrdinalIgnoreCase))
                        select new { ConflictDetectedAt = (DateTime)doc[""@metadata""][""Last-Modified""]}",
            };

            ApplicationModel.DatabaseCommands.PutIndexAsync(ConflictsIndexName, index, true).CatchIgnore();
        }

        protected override void OnViewUnloaded()
        {
            StopListeningForChanges();
        }

        private void ObserveSourceChanges()
        {
            if (!IsLoaded)
                return;

            StopListeningForChanges();

            var databaseModel = ApplicationModel.Database.Value;

            if (databaseModel != null)
            {
                changesSubscription =
                    databaseModel.IndexChanges.Where(i => i.Name.Equals(ConflictsIndexName, StringComparison.Ordinal))
                    .SampleResponsive(TimeSpan.FromSeconds(1))
                    .ObserveOnDispatcher()
                    .Subscribe(_ => ConflictDocuments.Refresh(RefreshMode.PermitStaleDataWhilstRefreshing));
            }
        }

        private void StopListeningForChanges()
        {
            if (changesSubscription != null)
                changesSubscription.Dispose();
        }
    }
}
