//-----------------------------------------------------------------------
// <copyright file="LocalClientTest.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System.IO;
using System.Reflection;
using System.Threading;
using Raven.Client.Client;
using Raven.Database.Config;
using Raven.Database.Extensions;
using Raven.Tests.Document;

namespace Raven.Tests
{
	public abstract class LocalClientTest
	{
		private string path;

        public EmbeddableDocumentStore NewDocumentStore()
		{
			/*path = Path.GetDirectoryName(Assembly.GetAssembly(typeof(DocumentStoreServerTests)).CodeBase);
			path = Path.Combine(path, "TestDb").Substring(6);*/
			path = Path.Combine(".", "TestDb");

            IOExtensions.DeleteDirectory(path);

            var documentStore = new EmbeddableDocumentStore()
			{
				Configuration = 
				{
					DataDirectory = path,
                    RunInUnreliableYetFastModeThatIsNotSuitableForProduction = true
				}

			};
			documentStore.Initialize();
			return documentStore;
		}

        public void WaitForIndexing(EmbeddableDocumentStore store)
        {
            while (store.DocumentDatabase.Statistics.StaleIndexes.Length > 0)
            {
                Thread.Sleep(100);
            }
        }
	}
}
