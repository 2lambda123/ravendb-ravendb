﻿using System.IO;
using System.Text;
using Voron.Debugging;
using Voron.Impl;
using Xunit;

namespace Voron.Tests
{
	public class DebugJournalTest
	{
		private const string debugJouralName = "debug_journal_test";

		public DebugJournalTest()
		{
			const string fileNameWithDebugJournalExtension = debugJouralName + ".djrs";

			if (File.Exists(fileNameWithDebugJournalExtension))
				File.Delete(fileNameWithDebugJournalExtension);
		}

		[Fact]
		public void Record_debug_journal_and_replay_it()
		{
			using (var env = new StorageEnvironment(StorageEnvironmentOptions.CreateMemoryOnly()))
			{

				env.DebugJournal = new DebugJournal(debugJouralName, env, true);
				using (var tx = env.NewTransaction(TransactionFlags.ReadWrite))
				{
					env.CreateTree(tx, "test-tree");
					tx.Commit();
				}

				using (var writeBatch = new WriteBatch())
				{
					var valueBuffer = new MemoryStream(Encoding.UTF8.GetBytes("testing testing 1!"));
					writeBatch.Add("foo", valueBuffer, "test-tree");

					valueBuffer = new MemoryStream(Encoding.UTF8.GetBytes("testing testing 1 2!"));
					writeBatch.Add("bar", valueBuffer, "test-tree");

					valueBuffer = new MemoryStream(Encoding.UTF8.GetBytes("testing testing 1 2 3!"));
					writeBatch.Add("foo-bar", valueBuffer, "test-tree");

					writeBatch.MultiAdd("multi-foo", "AA", "test-tree");
					env.Writer.Write(writeBatch);
				}

				using (var writeBatch = new WriteBatch())
				{
		
					writeBatch.MultiAdd("multi-foo", "BB", "test-tree");
					writeBatch.MultiAdd("multi-foo", "CC", "test-tree");

					writeBatch.Delete("foo-bar", "test-tree");
					env.Writer.Write(writeBatch);
				}

				using (var tx = env.NewTransaction(TransactionFlags.ReadWrite))
				{
					env.CreateTree(tx, "test-tree2");
					tx.Commit();
				}

				using (var writeBatch = new WriteBatch())
				{
					var valueBuffer = new MemoryStream(Encoding.UTF8.GetBytes("testing testing 1!"));
					writeBatch.Add("foo", valueBuffer, "test-tree2");

					valueBuffer = new MemoryStream(Encoding.UTF8.GetBytes("testing testing 1 2!"));
					writeBatch.Add("bar", valueBuffer, "test-tree2");

					valueBuffer = new MemoryStream(Encoding.UTF8.GetBytes("testing testing 1 2 3!"));
					writeBatch.Add("foo-bar", valueBuffer, "test-tree2");
					env.Writer.Write(writeBatch);
				}
			}

			using (var env = new StorageEnvironment(StorageEnvironmentOptions.CreateMemoryOnly()))
			{
				env.DebugJournal = DebugJournal.FromFile(debugJouralName, env);
				env.DebugJournal.Replay();

				using (var snapshot = env.CreateSnapshot())
				{
				    Assert.Equal("testing testing 1!",snapshot.Read("test-tree", "foo").Reader.ToStringValue());
				    Assert.Equal("testing testing 1 2!", snapshot.Read("test-tree", "bar").Reader.ToStringValue());

				    Assert.Equal("testing testing 1!", snapshot.Read("test-tree2", "foo").Reader.ToStringValue());
				    Assert.Equal("testing testing 1 2!", snapshot.Read("test-tree2", "bar").Reader.ToStringValue());
				    Assert.Equal("testing testing 1 2 3!", snapshot.Read("test-tree2", "foo-bar").Reader.ToStringValue());


					Assert.Equal(0,snapshot.ReadVersion("test-tree","foo-bar"));

					using (var iter = snapshot.MultiRead("test-tree","multi-foo"))
					{
						iter.Seek(Slice.BeforeAllKeys);
						Assert.Equal("AA",iter.CurrentKey.ToString());
						Assert.DoesNotThrow(() => iter.MoveNext());
						Assert.Equal("BB",iter.CurrentKey.ToString());
						Assert.DoesNotThrow(() => iter.MoveNext());
						Assert.Equal("CC",iter.CurrentKey.ToString());
					}
				}
			}			

		}

		
	}
}
