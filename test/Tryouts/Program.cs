/*
    Code reference - please DO NOT REMOVE:
         
    DebuggerAttachedTimeout.DisableLongTimespan = true;
    
    Console.WriteLine(Process.GetCurrentProcess().Id);
    Console.WriteLine();
    
    LoggingSource.Instance.SetupLogMode(LogMode.Information, @"c:\work\ravendb\logs");
 */

using System.Threading.Tasks;
using SlowTests.Client.Attachments;

namespace Tryouts
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            using (var test = new AttachmentsSession())
                await test.PutHashAttachment();
        }
    }
}
