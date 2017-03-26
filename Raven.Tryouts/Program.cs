using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Raven.Abstractions.Data;
using Raven.Abstractions.FileSystem;
using Raven.Abstractions.Smuggler;
using Raven.Database.Extensions;
using Raven.Json.Linq;
using Raven.Smuggler;
using Raven.Tests.Common;
using Raven.Tests.FileSystem;
using Raven.Tests.Raft.Client;
using Raven.Tests.Smuggler;
using Raven.Tests.Subscriptions;
using Xunit;
#if !DNXCORE50
using Raven.Tests.Sorting;
using Raven.SlowTests.RavenThreadPool;
using Raven.Tests.Core;
using Raven.Tests.Core.Commands;
using Raven.Tests.Issues;
using Raven.Tests.MailingList;
using Raven.Tests.FileSystem.ClientApi;
#endif

namespace Raven.Tryouts
{  
    
    public class Program
    {
        public static void Main(string[] args)
        {
            using (var test = new RavenDB_6626())
            {
                test.LoadOperation_should_not_throw().Wait();
            }
        }

        public static async Task AsyncMain()
        {
            //using (var store = new FilesStore()
            //{
            //    Url = "http://localhost:8080",
            //    DefaultFileSystem = "FS1"
            //}.Initialize())
            //{
            //    var ms = new MemoryStream();
            //    var buffer = new byte[1024];

            //    for (int i = 0; i < buffer.Length; i++)
            //    {
            //        buffer[i] = (byte)i;
            //    }

            //    ms.Write(buffer, 0, buffer.Length);
            //    ms.Position = 0;
            //    await store.AsyncFilesCommands.UploadAsync("file.txt", ms);

            //    var tasks = new Task[50];

            //    for (var i = 0; i < 15000; i += tasks.Length)
            //    {
            //        var sessio = store.OpenAsyncSession();

            //        for (var j = 0; j < tasks.Length; j++)
            //        {
            //            var targetNAme = "file" + (i + j).ToString() + ".txt";
            //            tasks[j] = store.AsyncFilesCommands.CopyAsync("file.txt", targetNAme);
            //        }

            //        Task.WaitAll(tasks);

            //        Console.WriteLine(i);
            //    }
            //}

            //var sp = Stopwatch.StartNew();

            //var smugglerApi = new SmugglerFilesApi();


            //await smugglerApi.ExportData(exportOptions: new SmugglerExportOptions<FilesConnectionStringOptions>()
            //{
            //    From = new FilesConnectionStringOptions()
            //    {
            //        DefaultFileSystem = "FS1",
            //        Url = "http://localhost:8080",

            //    },
            //    ToFile = "c:\\Temp\\export.ravendump",

            //});


            //Console.WriteLine(sp.ElapsedMilliseconds);


            var sp = Stopwatch.StartNew();
            try
            {
                var smugglerApi = new SmugglerFilesApi();
                await smugglerApi.ImportData(importOptions: new SmugglerImportOptions<FilesConnectionStringOptions>()
                {
                    To = new FilesConnectionStringOptions()
                    {
                        DefaultFileSystem = "FS2",
                        Url = "http://localhost:8080",

                    },
                    FromFile = "c:\\Temp\\export.ravendump",
                });
            }
            catch (Exception ex)
            {

                Console.WriteLine(ex);

                Console.WriteLine(ex.StackTrace);
            }

            Console.ReadLine();

            Console.WriteLine(sp.ElapsedMilliseconds);


        }
    }
}
