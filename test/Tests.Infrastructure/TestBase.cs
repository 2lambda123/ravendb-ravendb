﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Raven.Client;
using Raven.Client.Http;
using Raven.Client.Util;
using Raven.Server;
using Raven.Server.Commercial;
using Raven.Server.Config;
using Raven.Server.Config.Categories;
using Raven.Server.Config.Settings;
using Raven.Server.Documents;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow.Collections;
using Sparrow.Logging;
using Sparrow.Platform;
using Sparrow.Server.Platform;
using Sparrow.Threading;
using Sparrow.Utils;
using Tests.Infrastructure;
using Tests.Infrastructure.Utils;
using Xunit;
using Xunit.Abstractions;

namespace FastTests
{
    public abstract class TestBase : LinuxRaceConditionWorkAround, IAsyncLifetime
    {
        private static int _counter;

        private const string XunitConfigurationFile = "xunit.runner.json";

        private const string ServerName = "Raven.Tests.Core.Server";

        private static readonly ConcurrentSet<string> GlobalPathsToDelete = new ConcurrentSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly SemaphoreSlim ConcurrentTestsSemaphore;
        private readonly MultipleUseFlag _concurrentTestsSemaphoreTaken = new MultipleUseFlag();

        private readonly ConcurrentSet<string> _localPathsToDelete = new ConcurrentSet<string>(StringComparer.OrdinalIgnoreCase);

        private static RavenServer _globalServer;

        protected static bool IsGlobalServer(RavenServer server)
        {
            return _globalServer == server;
        }

        private RavenServer _localServer;

        protected bool IsGlobalOrLocalServer(RavenServer server)
        {
            return _globalServer == server || _localServer == server;
        }

        protected List<RavenServer> Servers = new List<RavenServer>();

        private static readonly object ServerLocker = new object();

        private bool _doNotReuseServer;

        private IDictionary<string, string> _customServerSettings;

        static TestBase()
        {
            LicenseManager.IgnoreProcessorAffinityChanges = true;
            NativeMemory.GetCurrentUnmanagedThreadId = () => (ulong)Pal.rvn_get_current_thread_id();
#if DEBUG2
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                if (args.Observed)
                    return;

                var e = args.Exception.ExtractSingleInnerException();

                var sb = new StringBuilder();
                sb.AppendLine("===== UNOBSERVED TASK EXCEPTION =====");
                sb.AppendLine(e.ExceptionToString(null));
                sb.AppendLine("=====================================");

                Console.WriteLine(sb.ToString());
            };
#endif
            if (PlatformDetails.RunningOnPosix == false &&
                PlatformDetails.Is32Bits) // RavenDB-13655
            {
                ThreadPool.SetMinThreads(25, 25);
                ThreadPool.SetMaxThreads(125, 125);
            }
            else
            {
                ThreadPool.SetMinThreads(250, 250);
            }

            var maxNumberOfConcurrentTests = Math.Max(ProcessorInfo.ProcessorCount / 2, 2);

            RequestExecutor.RemoteCertificateValidationCallback += (sender, cert, chain, errors) => true;

            var fileInfo = new FileInfo(XunitConfigurationFile);
            if (fileInfo.Exists)
            {
                using (var file = File.OpenRead(XunitConfigurationFile))
                using (var sr = new StreamReader(file))
                {
                    var json = JObject.Parse(sr.ReadToEnd());

                    if (json.TryGetValue("maxRunningTests", out var testsToken))
                        maxNumberOfConcurrentTests = testsToken.Value<int>();
                    else if (json.TryGetValue("maxParallelThreads", out var threadsToken))
                        maxNumberOfConcurrentTests = threadsToken.Value<int>();
                }
            }

            Console.WriteLine("Max number of concurrent tests is: " + maxNumberOfConcurrentTests);
            ConcurrentTestsSemaphore = new SemaphoreSlim(maxNumberOfConcurrentTests, maxNumberOfConcurrentTests);
        }

        protected TestBase(ITestOutputHelper output) : base(output)
        {
            TestResourcesAnalyzer.Start(Context);
        }

        protected string GetDatabaseName([CallerMemberName] string caller = null)
        {
            if (caller != null && caller.Contains(".ctor"))
                throw new InvalidOperationException(
                    $"{nameof(GetDatabaseName)} was invoked from within {GetType().Name} constructor. This is an indication that you're trying to generate" +
                    " a database within a test class constructor. This is forbidden because this database will be generated but the test won't run until" +
                    $" it gets the semaphore at {nameof(InitializeAsync)} also the constructor is invoked per test method and it is not shared between tests" +
                    " so there is no value in generating the database from the constructor.");

            var name = caller != null ? $"{caller}_{Interlocked.Increment(ref _counter)}" : Guid.NewGuid().ToString("N");
            return name;
        }

        public void DoNotReuseServer(IDictionary<string, string> customSettings = null)
        {
            _customServerSettings = customSettings;
            _doNotReuseServer = true;
        }

        protected static TestCertificatesHolder _selfSignedCertificates;

        protected TestCertificatesHolder GenerateAndSaveSelfSignedCertificate(bool createNew = false)
        {
            var selfSignedCertificatePaths = _selfSignedCertificates;
            if (selfSignedCertificatePaths != null && createNew == false)
                return ReturnCertificatesHolder(selfSignedCertificatePaths);

            lock (typeof(TestBase))
            {
                selfSignedCertificatePaths = _selfSignedCertificates;
                if (selfSignedCertificatePaths == null || createNew)
                    _selfSignedCertificates = selfSignedCertificatePaths = Generate();

                return ReturnCertificatesHolder(selfSignedCertificatePaths);
            }

            TestCertificatesHolder ReturnCertificatesHolder(TestCertificatesHolder certificates)
            {
                return new TestCertificatesHolder(certificates, GetTempFileName);
            }

            TestCertificatesHolder Generate()
            {
                var log = new StringBuilder();
                byte[] certBytes;
                try
                {
                    certBytes = CertificateUtils.CreateSelfSignedTestCertificate(Environment.MachineName, "RavenTestsServer", log);
                }
                catch (Exception e)
                {
                    throw new CryptographicException($"Unable to generate the test certificate for the machine '{Environment.MachineName}'. Log: {log}", e);
                }

                X509Certificate2 serverCertificate;
                try
                {
                    serverCertificate = new X509Certificate2(certBytes, (string)null, X509KeyStorageFlags.MachineKeySet);
                }
                catch (Exception e)
                {
                    throw new CryptographicException($"Unable to load the test certificate for the machine '{Environment.MachineName}'. Log: {log}", e);
                }

                if (certBytes.Length == 0)
                    throw new CryptographicException($"Test certificate length is 0 bytes. Machine: '{Environment.MachineName}', Log: {log}");

                string serverCertificatePath = null;
                try
                {
                    serverCertificatePath = Path.GetTempFileName();
                    File.WriteAllBytes(serverCertificatePath, certBytes);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Failed to write the test certificate to a temp file." +
                                                        $"tempFileName = {serverCertificatePath}" +
                                                        $"certBytes.Length = {certBytes.Length}" +
                                                        $"MachineName = {Environment.MachineName}.", e);
                }

                GlobalPathsToDelete.Add(serverCertificatePath);

                SecretProtection.ValidatePrivateKey(serverCertificatePath, null, certBytes, out var pk);

                var clientCertificate1Path = GenerateClientCertificate(1, serverCertificate, pk);
                var clientCertificate2Path = GenerateClientCertificate(2, serverCertificate, pk);
                var clientCertificate3Path = GenerateClientCertificate(3, serverCertificate, pk);

                return new TestCertificatesHolder(serverCertificatePath, clientCertificate1Path, clientCertificate2Path, clientCertificate3Path);
            }

            string GenerateClientCertificate(int index, X509Certificate2 serverCertificate, Org.BouncyCastle.Pkcs.AsymmetricKeyEntry pk)
            {
                CertificateUtils.CreateSelfSignedClientCertificate(
                    $"{Environment.MachineName}_CC_{index}",
                    new RavenServer.CertificateHolder
                    {
                        Certificate = serverCertificate,
                        PrivateKey = pk
                    },
                    out var certBytes);

                string clientCertificatePath = null;
                try
                {
                    clientCertificatePath = Path.GetTempFileName();
                    File.WriteAllBytes(clientCertificatePath, certBytes);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Failed to write the test certificate to a temp file." +
                                                        $"tempFileName = {clientCertificatePath}" +
                                                        $"certBytes.Length = {certBytes.Length}" +
                                                        $"MachineName = {Environment.MachineName}.", e);
                }

                GlobalPathsToDelete.Add(clientCertificatePath);

                return clientCertificatePath;
            }
        }

        protected string GetTempFileName()
        {
            var tmp = Path.GetTempFileName();

            _localPathsToDelete.Add(tmp);

            return tmp;
        }

        public async Task<DocumentDatabase> GetDatabase(string databaseName)
        {
            var database = await Server.ServerStore.DatabasesLandlord.TryGetOrCreateResourceStore(databaseName).ConfigureAwait(false);
            if (database == null)
            {
                // Throw and get more info why database is null
                using (Server.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                {
                    context.OpenReadTransaction();
                    var lastCommit = Server.ServerStore.Engine.GetLastCommitIndex(context);
                    var doc = Server.ServerStore.Cluster.Read(context, "db/" + databaseName.ToLowerInvariant());
                    throw new InvalidOperationException("For " + databaseName + ". Database is null and database record is: " + (doc == null ? "null" : doc.ToString()) + " Last commit: " + lastCommit);
                }
            }
            return database;
        }

        public RavenServer Server
        {
            get
            {
                if (_localServer != null)
                    return _localServer;

                if (_doNotReuseServer)
                {
                    bool runInMemory = true;

                    if (_customServerSettings != null && _customServerSettings.ContainsKey(RavenConfiguration.GetKey(x => x.Core.RunInMemory)))
                        runInMemory = bool.Parse(_customServerSettings[RavenConfiguration.GetKey(x => x.Core.RunInMemory)]);

                    UseNewLocalServer(runInMemory: runInMemory);
                    _doNotReuseServer = false;

                    return _localServer;
                }

                if (_globalServer != null)
                {
                    if (_globalServer.Disposed)
                        throw new ObjectDisposedException("Someone disposed the global server!");
                    _localServer = _globalServer;

                    return _localServer;
                }
                lock (ServerLocker)
                {
                    if (_globalServer == null || _globalServer.Disposed)
                    {
                        var globalServer = GetNewServer(new ServerCreationOptions { RegisterForDisposal = false }, "Global");
                        using (var currentProcess = Process.GetCurrentProcess())
                        {
                            Console.WriteLine(
                                $"\tTo attach debugger to test process ({(PlatformDetails.Is32Bits ? "x86" : "x64")}), use proc-id: {currentProcess.Id}. Url {globalServer.WebUrl}");
                        }

                        AssemblyLoadContext.Default.Unloading += UnloadServer;
                        _globalServer = globalServer;
                    }
                    _localServer = _globalServer;
                }
                return _globalServer;
            }
        }

        private static void CheckServerLeak()
        {
            foreach (var leakedServer in LeakedServers)
            {
                if (leakedServer.Key.Disposed)
                    continue;

                Console.WriteLine($"[ Leak!! ] The test {leakedServer.Value} leaks a server.");
            }
        }

        private static void UnloadServer(AssemblyLoadContext obj)
        {
            try
            {
                lock (ServerLocker)
                {
                    var copyGlobalServer = _globalServer;
                    _globalServer = null;
                    if (copyGlobalServer == null)
                        return;

                    try
                    {
                        using (copyGlobalServer.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                        using (context.OpenReadTransaction())
                        {
                            var databases = copyGlobalServer
                                .ServerStore
                                .Cluster
                                .ItemsStartingWith(context, Constants.Documents.Prefix, 0, int.MaxValue)
                                .ToList();

                            if (databases.Count > 0)
                            {
                                var sb = new StringBuilder();
                                sb.AppendLine("List of non-deleted databases:");

                                foreach (var t in databases)
                                {
                                    var databaseName = t.ItemName.Substring(Constants.Documents.Prefix.Length);

                                    try
                                    {
                                        AsyncHelpers.RunSync(() => copyGlobalServer.ServerStore.DeleteDatabaseAsync(databaseName, hardDelete: true, null, Guid.NewGuid().ToString()));
                                    }
                                    catch (Exception)
                                    {
                                        // ignored
                                    }

                                    sb
                                        .Append("- ")
                                        .AppendLine(databaseName);
                                }

                                Console.WriteLine(sb.ToString());
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Could not retrieve list of non-deleted databases. Exception: {e}");
                    }

                    copyGlobalServer.Dispose();

                    GC.Collect(2);
                    GC.WaitForPendingFinalizers();

                    var exceptionAggregator = new ExceptionAggregator("Failed to cleanup test databases");

                    RavenTestHelper.DeletePaths(GlobalPathsToDelete, exceptionAggregator);

                    exceptionAggregator.ThrowIfNeeded();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                TestResourcesAnalyzer.Complete();
                CheckServerLeak();
            }
        }

        public void UseNewLocalServer(IDictionary<string, string> customSettings = null, bool runInMemory = true, string customConfigPath = null, [CallerMemberName]string caller = null)
        {
            if (_localServer != _globalServer && _globalServer != null)
                _localServer?.Dispose();

            var co = new ServerCreationOptions
            {
                CustomSettings = customSettings ?? _customServerSettings,
                RunInMemory = runInMemory,
                CustomConfigPath = customConfigPath,
                RegisterForDisposal = false
            };
            _localServer = GetNewServer(co, caller);
        }

        private readonly object _getNewServerSync = new object();
        protected List<RavenServer> ServersForDisposal = new List<RavenServer>();

        public class ServerCreationOptions
        {
            private IDictionary<string, string> _customSettings;

            public IDictionary<string, string> CustomSettings
            {
                get => _customSettings;
                set
                {
                    AssertNotFrozen();
                    _customSettings = value;
                }
            }

            private bool _deletePrevious = true;

            public bool DeletePrevious
            {
                get => _deletePrevious;
                set
                {
                    AssertNotFrozen();
                    _deletePrevious = value;
                }
            }

            private bool _runInMemory = true;

            public bool RunInMemory
            {
                get => _runInMemory;
                set
                {
                    AssertNotFrozen();
                    _runInMemory = value;
                }
            }

            private string _dataDirectory;

            public string DataDirectory
            {
                get => _dataDirectory;
                set
                {
                    AssertNotFrozen();
                    _dataDirectory = value;
                }
            }

            private string _customConfigPath;

            public string CustomConfigPath
            {
                get => _customConfigPath;
                set
                {
                    AssertNotFrozen();
                    _customConfigPath = value;
                }
            }

            private bool _registerForDisposal = true;

            public bool RegisterForDisposal
            {
                get => _registerForDisposal;
                set
                {
                    AssertNotFrozen();
                    _registerForDisposal = value;
                }
            }

            private string _nodeTag;

            public string NodeTag
            {
                get => _nodeTag;
                set
                {
                    AssertNotFrozen();
                    _nodeTag = value;
                }
            }

            private readonly bool _frozen;

            private void AssertNotFrozen()
            {
                if (_frozen)
                    throw new InvalidOperationException("ServerCreationOptions are frozen and cannot be changed.");
            }

            public ServerCreationOptions(bool frozen = false)
            {
                _frozen = frozen;
            }

            private static readonly Lazy<ServerCreationOptions> _default = new Lazy<ServerCreationOptions>(() => new ServerCreationOptions(frozen: true));
            public static ServerCreationOptions Default => _default.Value;

            public Action<ServerStore> BeforeDatabasesStartup;
        }

        private static readonly ConcurrentDictionary<RavenServer, string> LeakedServers = new ConcurrentDictionary<RavenServer, string>();
        protected virtual RavenServer GetNewServer(ServerCreationOptions options = null, [CallerMemberName]string caller = null)
        {
            if (options == null)
            {
                options = ServerCreationOptions.Default;
            }

            lock (_getNewServerSync)
            {
                var configuration = RavenConfiguration.CreateForServer(Guid.NewGuid().ToString(), options.CustomConfigPath);

                configuration.SetSetting(RavenConfiguration.GetKey(x => x.Replication.ReplicationMinimalHeartbeat), "1");
                configuration.SetSetting(RavenConfiguration.GetKey(x => x.Replication.RetryReplicateAfter), "3");
                configuration.SetSetting(RavenConfiguration.GetKey(x => x.Replication.RetryMaxTimeout), "3");
                configuration.SetSetting(RavenConfiguration.GetKey(x => x.Cluster.AddReplicaTimeout), "10");

                if (options.CustomSettings != null)
                {
                    foreach (var setting in options.CustomSettings)
                        configuration.SetSetting(setting.Key, setting.Value);
                }

                var hasServerUrls = options.CustomSettings != null && options.CustomSettings.ContainsKey(RavenConfiguration.GetKey(x => x.Core.ServerUrls));
                var hasDataDirectory = options.CustomSettings != null && options.CustomSettings.ContainsKey(RavenConfiguration.GetKey(x => x.Core.DataDirectory));
                var hasFeaturesAvailability = options.CustomSettings != null && options.CustomSettings.ContainsKey(RavenConfiguration.GetKey(x => x.Core.FeaturesAvailability));

                configuration.Initialize();

                configuration.Logs.Mode = LogMode.None;
                configuration.Server.Name = ServerName;
                configuration.Core.RunInMemory = options.RunInMemory;
                configuration.Server.MaxTimeForTaskToWaitForDatabaseToLoad = new TimeSetting(60, TimeUnit.Seconds);
                configuration.Licensing.EulaAccepted = true;

                if (hasServerUrls == false)
                    configuration.Core.ServerUrls = new[] { "http://127.0.0.1:0" };

                if (hasDataDirectory == false)
                {
                    string dataDirectory = null;
                    if (options.DataDirectory == null)
                        dataDirectory = NewDataPath(prefix: $"GetNewServer-{options.NodeTag}", forceCreateDir: true);
                    else
                    {
                        if (Path.IsPathRooted(options.DataDirectory) == false)
                            throw new InvalidOperationException($"{nameof(ServerCreationOptions)}.{nameof(ServerCreationOptions.DataDirectory)} path needs to be rooted. Was: {options.DataDirectory}");

                        dataDirectory = options.DataDirectory;
                    }

                    configuration.Core.DataDirectory = configuration.Core.DataDirectory.Combine(dataDirectory);
                }
                else if (options.DataDirectory != null)
                    throw new InvalidOperationException($"You cannot set DataDirectory in both, {nameof(ServerCreationOptions)}.{nameof(ServerCreationOptions.CustomSettings)} and {nameof(ServerCreationOptions)}.{nameof(ServerCreationOptions.DataDirectory)}");

                if (hasFeaturesAvailability == false)
                    configuration.Core.FeaturesAvailability = FeaturesAvailability.Experimental;

                if (options.DeletePrevious)
                    IOExtensions.DeleteDirectory(configuration.Core.DataDirectory.FullPath);

                var server = new RavenServer(configuration)
                {
                    ThrowOnLicenseActivationFailure = true,
                    DebugTag = caller
                };

                if (options.BeforeDatabasesStartup != null)
                    server.ServerStore.DatabasesLandlord.ForTestingPurposesOnly().BeforeHandleClusterDatabaseChanged = options.BeforeDatabasesStartup;

                server.Initialize();
                server.ServerStore.ValidateFixedPort = false;

                if (options.RegisterForDisposal)
                    ServersForDisposal.Add(server);

                if (LeakedServers.TryAdd(server, server.DebugTag))
                    server.AfterDisposal += () => LeakedServers.TryRemove(server, out _);

                return server;
            }
        }

        protected static string UseFiddlerUrl(string url)
        {
            if (Debugger.IsAttached && Process.GetProcessesByName("fiddler").Any())
                url = url.Replace("127.0.0.1", "localhost.fiddler");

            return url;
        }

        protected static string[] UseFiddler(string url)
        {
            if (Debugger.IsAttached && Process.GetProcessesByName("fiddler").Any())
                url = url.Replace("127.0.0.1", "localhost.fiddler");

            return new[] { url };
        }

        protected static void OpenBrowser(string url)
        {
            Console.WriteLine(url);

            if (PlatformDetails.RunningOnPosix == false)
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"Stop & look at studio\" \"{url}\""));
                return;
            }

            if (PlatformDetails.RunningOnMacOsx)
            {
                Process.Start("open", url);
                return;
            }

            Process.Start("xdg-open", url);
        }

        protected string NewDataPath([CallerMemberName] string prefix = null, string suffix = null, bool forceCreateDir = false)
        {
            if (suffix != null)
                prefix += suffix;
            var path = RavenTestHelper.NewDataPath(prefix, 0, forceCreateDir);

            GlobalPathsToDelete.Add(path);
            _localPathsToDelete.Add(path);

            return path;
        }

        protected abstract void Dispose(ExceptionAggregator exceptionAggregator);

        public override void Dispose()
        {
            GC.SuppressFinalize(this);

            if (_concurrentTestsSemaphoreTaken.Lower())
                ConcurrentTestsSemaphore.Release();

            var exceptionAggregator = new ExceptionAggregator("Could not dispose test");

            var testOutcomeAnalyzer = new TestOutcomeAnalyzer(Context);
            var threwRavenTimeoutException = testOutcomeAnalyzer.ThrewRavenTimeoutException();

            Dispose(exceptionAggregator);

            if (threwRavenTimeoutException && _globalServer != null && _globalServer.Disposed == false)
                exceptionAggregator.Execute(() => DebugPackageHandler.DownloadAndSave(_globalServer, Context));

            if (_localServer != null && _localServer != _globalServer)
            {
                exceptionAggregator.Execute(() =>
                {
                    _localServer.Dispose();
                    _localServer = null;
                });

                if (threwRavenTimeoutException)
                    exceptionAggregator.Execute(() => DebugPackageHandler.DownloadAndSave(_localServer, Context));
            }

            var firstServerForDisposal = ServersForDisposal.FirstOrDefault();
            if (threwRavenTimeoutException && firstServerForDisposal != null)
                exceptionAggregator.Execute(() => DebugPackageHandler.DownloadAndSave(firstServerForDisposal, Context));

            foreach (var server in ServersForDisposal)
            {
                exceptionAggregator.Execute(() =>
                {
                    server.Dispose();
                });
            }
            
            ServersForDisposal = null;

            RavenTestHelper.DeletePaths(_localPathsToDelete, exceptionAggregator);

            TestResourcesAnalyzer.End(Context);

            exceptionAggregator.ThrowIfNeeded();
        }

        public Task InitializeAsync()
        {
            return ConcurrentTestsSemaphore.WaitAsync()
                .ContinueWith(x => _concurrentTestsSemaphoreTaken.Raise());
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
