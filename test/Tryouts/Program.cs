using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FastTests.Server.Documents.Queries.Parser;
using FastTests.Voron.Backups;
using FastTests.Voron.Compaction;
using SlowTests.Authentication;
using SlowTests.Bugs.MapRedue;
using SlowTests.Client;
using SlowTests.Client.Attachments;
using SlowTests.Issues;
using SlowTests.MailingList;
using Sparrow.Logging;
using StressTests.Client.Attachments;

namespace Tryouts
{
    public static class Program
    {
        public static void Main(string[] args)
        {

            var foobar = (3, "ABC", 5.77, new Jordan.User{ FirstName = "John" });

            var tuple = foobar as ITuple;
            for(int i = 0; i < tuple.Length; i++)
                Console.WriteLine(tuple[i]);

            //try
            //{
            //    using (var test = new RavenDB_11734())
            //    {
            //        await test.Index_Queries_Should_Not_Return_Deleted_Documents();
            //    }
            //}
            //catch (Exception e)
            //{
            //    Console.WriteLine(e);
            //}


        }
    }
}
