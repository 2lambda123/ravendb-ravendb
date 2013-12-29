﻿namespace Raven.Database.Storage.Voron.Impl
{
	using System;
	using System.IO;

	using Raven.Database.Config;

	using global::Voron;
	using global::Voron.Impl;

	public class MemoryMapPersistenceSource : IPersistenceSource
	{
		private readonly InMemoryRavenConfiguration configuration;
	    private const string PAGER_FILENAME = "Raven.voron";

		private readonly string directoryPath;

		private readonly string filePath;

		public MemoryMapPersistenceSource(InMemoryRavenConfiguration configuration)
		{
			this.configuration = configuration;
			directoryPath = configuration.DataDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            filePath = directoryPath;
		    var filePathFolder = new DirectoryInfo(filePath);
		    if (filePathFolder.Exists == false)
		        filePathFolder.Create();

			Initialize();
		}

		public StorageEnvironmentOptions Options { get; private set; }

		public bool CreatedNew { get; private set; }

		private void Initialize()
		{
		    var storageFilePath = Path.Combine(filePath, PAGER_FILENAME);

			if (Directory.Exists(directoryPath))
			{
				CreatedNew = !File.Exists(storageFilePath);
			}
			else
			{
				Directory.CreateDirectory(directoryPath);
				CreatedNew = true;
			}

		    Options = new StorageEnvironmentOptions.DirectoryStorageEnvironmentOptions(storageFilePath);
		}

		public void Dispose()
		{
			if (Options != null)
				Options.Dispose();
		}
	}
}