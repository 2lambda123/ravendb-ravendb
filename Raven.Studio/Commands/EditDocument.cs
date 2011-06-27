﻿namespace Raven.Studio.Commands
{
    using System.Collections;
    using System.ComponentModel.Composition;
    using System.Linq;
    using Caliburn.Micro;
	using Features.Documents;
	using Messages;

	[Export]
	public class EditDocument
	{
		readonly IEventAggregator events;

		[ImportingConstructor]
		public EditDocument(IEventAggregator events)
		{
			this.events = events;
		}

		public void Execute(DocumentViewModel document)
		{
			var editScreen = IoC.Get<EditDocumentViewModel>();
			editScreen.Initialize(document.JsonDocument);

			events.Publish(new DatabaseScreenRequested(() => editScreen));
		}
	}
}