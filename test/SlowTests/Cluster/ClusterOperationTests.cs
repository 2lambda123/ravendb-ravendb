﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FastTests;
using Newtonsoft.Json;
using Raven.Client.Documents;
using Raven.Client.Documents.Changes;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.Identities;
using Raven.Client.Exceptions;
using Raven.Client.Extensions;
using Raven.Client.Http;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Commands;
using Raven.Client.ServerWide.Operations;
using Raven.Server;
using Raven.Server.Commercial;
using Raven.Server.Config;
using Raven.Server.Config.Settings;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Commands;
using Raven.Server.Utils;
using Raven.Tests.Core.Utils.Entities;
using SlowTests.Authentication;
using Sparrow.Json;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Cluster
{
    public class ClusterOperationTests : ClusterTestBase
    {
        public ClusterOperationTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task ReorderDatabaseNodes()
        {
            var db = "ReorderDatabaseNodes";
            var (_, leader) = await CreateRaftCluster(3);
            await CreateDatabaseInCluster(db, 3, leader.WebUrl);
            using (var store = new DocumentStore
            {
                Database = db,
                Urls = new[] { leader.WebUrl }
            }.Initialize())
            {
                await ReverseOrderSuccessfully(store, db);
                await FailSuccessfully(store, db);
            }
        }

        public static async Task FailSuccessfully(IDocumentStore store, string db)
        {
            var ex = await Assert.ThrowsAsync<RavenException>(async () =>
            {
                await store.Maintenance.Server.SendAsync(new ReorderDatabaseMembersOperation(db, new List<string>()
                {
                    "A",
                    "B"
                }));
            });
            Assert.True(ex.InnerException is ArgumentException);
            ex = await Assert.ThrowsAsync<RavenException>(async () =>
            {
                await store.Maintenance.Server.SendAsync(new ReorderDatabaseMembersOperation(db, new List<string>()
                {
                    "C",
                    "B",
                    "A",
                    "F"
                }));
            });
            Assert.True(ex.InnerException is ArgumentException);
        }

        [Fact]
        public async Task ClusterWideIdentity()
        {
            var db = "ClusterWideIdentity";
            var (_, leader) = await CreateRaftCluster(2);
            await CreateDatabaseInCluster(db, 2, leader.WebUrl);
            var nonLeader = Servers.First(x => ReferenceEquals(x, leader) == false);
            using (var store = new DocumentStore
            {
                Database = db,
                Urls = new[] { nonLeader.WebUrl }
            }.Initialize())
            {
                using (var session = store.OpenAsyncSession())
                {
                    var result = store.Maintenance.SendAsync(new SeedIdentityForOperation("users", 1990));
                    Assert.Equal(1990, result.Result);

                    var user = new User
                    {
                        Name = "Adi",
                        LastName = "Async"
                    };
                    await session.StoreAsync(user, "users|");
                    await session.SaveChangesAsync();
                    var id = session.Advanced.GetDocumentId(user);
                    Assert.Equal("users/1991", id);
                }
            }
        }

        [Fact]
        public async Task NextIdentityForOperationShouldBroadcast()
        {
            DebuggerAttachedTimeout.DisableLongTimespan = true;
            DefaultClusterSettings[RavenConfiguration.GetKey(x => x.Cluster.RotatePreferredNodeGraceTime)] = "15";
            DefaultClusterSettings[RavenConfiguration.GetKey(x => x.Cluster.MoveToRehabGraceTime)] = "15";

            var database = GetDatabaseName();
            var numberOfNodes = 3;
            var cluster = await CreateRaftCluster(numberOfNodes);
            var createResult = await CreateDatabaseInClusterInner(new DatabaseRecord(database), numberOfNodes, cluster.Leader.WebUrl, null);

            using (var store = new DocumentStore
            {
                Database = database,
                Urls = new[] { cluster.Leader.WebUrl }
            }.Initialize())
            {

                var re = store.GetRequestExecutor(database);
                var result = store.Maintenance.ForDatabase(database).Send(new NextIdentityForOperation("person|"));
                Assert.Equal(1, result);

                var preferred = await re.GetPreferredNode();
                var tag = preferred.Item2.ClusterTag;
                var server = createResult.Servers.Single(s => s.ServerStore.NodeTag == tag);
                server.ServerStore.InitializationCompleted.Reset(true);
                server.ServerStore.Initialized = false;
                server.ServerStore.Engine.CurrentLeader?.StepDown();

                var sp = Stopwatch.StartNew();
                result = store.Maintenance.ForDatabase(database).Send(new NextIdentityForOperation("person|"));
                Assert.True(sp.Elapsed < TimeSpan.FromSeconds(10));
                var newPreferred = await re.GetPreferredNode();

                Assert.NotEqual(tag, newPreferred.Item2.ClusterTag);
                Assert.Equal(2, result);
            }
        }

        [Fact]
        public async Task NextIdentityForOperationShouldBroadcastAndFail()
        {
            DebuggerAttachedTimeout.DisableLongTimespan = true;

            var database = GetDatabaseName();
            var numberOfNodes = 3;
            var cluster = await CreateRaftCluster(numberOfNodes);
            var createResult = await CreateDatabaseInClusterInner(new DatabaseRecord(database), numberOfNodes, cluster.Leader.WebUrl, null);

            using (var store = new DocumentStore
            {
                Database = database,
                Urls = new[] { cluster.Leader.WebUrl }
            }.Initialize())
            {
                var result = store.Maintenance.ForDatabase(database).Send(new NextIdentityForOperation("person|"));
                Assert.Equal(1, result);

                var node = createResult.Servers.First(n => n != cluster.Leader);
                node.ServerStore.InitializationCompleted.Reset(true);
                node.ServerStore.Initialized = false;

                await ActionWithLeader((l) => DisposeServerAndWaitForFinishOfDisposalAsync(l));

                using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    await Task.WhenAll(createResult.Servers.Where(s => s.Disposed == false).Select(s => s.ServerStore.WaitForState(RachisState.Candidate, cancel.Token)));
                }

                var sp = Stopwatch.StartNew();
                var ex = Assert.Throws<AllTopologyNodesDownException>(() => result = store.Maintenance.ForDatabase(database).Send(new NextIdentityForOperation("person|")));
                Assert.True(sp.Elapsed < TimeSpan.FromSeconds(45));

                var ae = (AggregateException)ex.InnerException;
                Assert.NotNull(ae);

                var exceptionTypes = new List<Type>{
                    typeof(HttpRequestException),  // the disposed node
                    typeof(TimeoutException), // the hang node
                    typeof(RavenException) // the last active one (no leader)
                };

                Assert.Contains(ae.InnerExceptions[0].InnerException.GetType(), exceptionTypes);
                Assert.Contains(ae.InnerExceptions[1].InnerException.GetType(), exceptionTypes);
                Assert.Contains(ae.InnerExceptions[2].InnerException.GetType(), exceptionTypes);
            }
        }

        [Fact]
        public async Task PreferredNodeShouldBeRestoredAfterBroadcast()
        {
            DebuggerAttachedTimeout.DisableLongTimespan = true;
            DefaultClusterSettings[RavenConfiguration.GetKey(x => x.Cluster.RotatePreferredNodeGraceTime)] = "15";
            DefaultClusterSettings[RavenConfiguration.GetKey(x => x.Cluster.MoveToRehabGraceTime)] = "15";

            var database = GetDatabaseName();
            var numberOfNodes = 3;
            var cluster = await CreateRaftCluster(numberOfNodes);
            var createResult = await CreateDatabaseInClusterInner(new DatabaseRecord(database), numberOfNodes, cluster.Leader.WebUrl, null);

            using (var store = new DocumentStore
            {
                Database = database,
                Urls = new[] { cluster.Leader.WebUrl }
            }.Initialize())
            {
                var re = store.GetRequestExecutor(database);
                var preferred = await re.GetPreferredNode();
                var tag = preferred.Item2.ClusterTag;

                var result = store.Maintenance.ForDatabase(database).Send(new NextIdentityForOperation("person|"));
                Assert.Equal(1, result);

                preferred = await re.GetPreferredNode();
                Assert.Equal(tag, preferred.Item2.ClusterTag);

                var server = createResult.Servers.Single(s => s.ServerStore.NodeTag == tag);
                server.ServerStore.InitializationCompleted.Reset(true);
                server.ServerStore.Initialized = false;
                server.ServerStore.Engine.CurrentLeader?.StepDown();
                var sp = Stopwatch.StartNew();
                result = store.Maintenance.ForDatabase(database).Send(new NextIdentityForOperation("person|"));
                sp.Stop();
                Assert.True(sp.Elapsed < TimeSpan.FromSeconds(10));

                var newPreferred = await re.GetPreferredNode();
                Assert.NotEqual(tag, newPreferred.Item2.ClusterTag);
                Assert.Equal(2, result);

                server.ServerStore.Initialized = true;

                var current = WaitForValue(() =>
                {
                    var p = re.GetPreferredNode().Result;

                    return p.Item2.ClusterTag;
                }, tag);

                Assert.Equal(tag, current);
            }
        }

        [Fact]
        public async Task ChangesApiFailOver()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var db = "ChangesApiFailOver_Test";
                var topology = new DatabaseTopology { DynamicNodesDistribution = true };
                var (_, leader) = await CreateRaftCluster(3,
                    customSettings: new Dictionary<string, string>()
                    {
                        [RavenConfiguration.GetKey(x => x.Cluster.AddReplicaTimeout)] = "1",
                        [RavenConfiguration.GetKey(x => x.Cluster.MoveToRehabGraceTime)] = "0",
                        [RavenConfiguration.GetKey(x => x.Cluster.StabilizationTime)] = "1",
                        [RavenConfiguration.GetKey(x => x.Cluster.ElectionTimeout)] = "50"
                    });

                await CreateDatabaseInCluster(new DatabaseRecord { DatabaseName = db, Topology = topology }, 2, leader.WebUrl);

                using (var store = new DocumentStore { Database = db, Urls = new[] { leader.WebUrl } }.Initialize())
                {
                    var list = new BlockingCollection<DocumentChange>();
                    var taskObservable = store.Changes();
                    await taskObservable.EnsureConnectedNow().WithCancellation(cts.Token);
                    var observableWithTask = taskObservable.ForDocument("users/1");
                    observableWithTask.Subscribe(list.Add);
                    await observableWithTask.EnsureSubscribedNow().WithCancellation(cts.Token);

                    using (var session = store.OpenAsyncSession())
                    {
                        await session.StoreAsync(new User(), "users/1", cts.Token);
                        await session.SaveChangesAsync(cts.Token);
                    }

                    WaitForDocument(store, "users/1");

                    var value = await WaitForValueAsync(() => list.Count, 1);
                    Assert.Equal(1, value);

                    var currentUrl = store.GetRequestExecutor().Url;
                    RavenServer toDispose = null;
                    RavenServer workingServer = null;

                    DisposeCurrentServer(currentUrl, ref toDispose, ref workingServer);

                    await taskObservable.EnsureConnectedNow().WithCancellation(cts.Token);

                    await WaitForTopologyStabilizationAsync(db, workingServer, 1, 2).WithCancellation(cts.Token);

                    using (var session = store.OpenAsyncSession())
                    {
                        await session.StoreAsync(new User(), "users/1", cts.Token);
                        await session.SaveChangesAsync(cts.Token);
                    }

                    value = await WaitForValueAsync(() => list.Count, 2);
                    Assert.Equal(2, value);

                    currentUrl = store.GetRequestExecutor().Url;
                    DisposeCurrentServer(currentUrl, ref toDispose, ref workingServer);

                    await taskObservable.EnsureConnectedNow().WithCancellation(cts.Token);

                    await WaitForTopologyStabilizationAsync(db, workingServer, 2, 1).WithCancellation(cts.Token);

                    using (var session = store.OpenSession())
                    {
                        session.Store(new User(), "users/1");
                        session.SaveChanges();
                    }

                    value = await WaitForValueAsync(() => list.Count, 3);
                    Assert.Equal(3, value);
                }
            }
        }

        [Fact]
        public async Task ChangesApiReorderDatabaseNodes()
        {
            var db = "ReorderDatabaseNodes";
            var (_, leader) = await CreateRaftCluster(2);
            await CreateDatabaseInCluster(db, 2, leader.WebUrl);
            using (var store = new DocumentStore
            {
                Database = db,
                Urls = new[] { leader.WebUrl }
            }.Initialize())
            {
                var list = new BlockingCollection<DocumentChange>();
                var taskObservable = store.Changes();
                await taskObservable.EnsureConnectedNow();
                var observableWithTask = taskObservable.ForDocument("users/1");
                observableWithTask.Subscribe(list.Add);
                await observableWithTask.EnsureSubscribedNow();

                using (var session = store.OpenSession())
                {
                    session.Store(new User(), "users/1");
                    session.SaveChanges();
                }
                string url1 = store.GetRequestExecutor().Url;
                Assert.True(WaitForDocument(store, "users/1"));
                var value = WaitForValue(() => list.Count, 1);
                Assert.Equal(1, value);


                await ReverseOrderSuccessfully(store, db);

                var value2 = WaitForValue(() =>
                {
                    string url2 = store.GetRequestExecutor().Url;
                    return (url1 != url2);
                }, true);

                using (var session = store.OpenSession())
                {
                    session.Store(new User(), "users/1");
                    session.SaveChanges();
                }
                value = WaitForValue(() => list.Count, 2);
                Assert.Equal(2, value);
            }
        }

        private void DisposeCurrentServer(string currnetUrl, ref RavenServer toDispose, ref RavenServer workingServer)
        {
            foreach (var server in Servers)
            {
                if (server.WebUrl == currnetUrl)
                {
                    toDispose = server;
                    continue;
                }
                if (server.Disposed != true)
                    workingServer = server;
            }
            DisposeServerAndWaitForFinishOfDisposal(toDispose);
        }

        private async Task WaitForTopologyStabilizationAsync(string s, RavenServer workingServer, int rehabCount, int memberCount)
        {
            using (var tempStore = new DocumentStore
            {
                Database = s,
                Urls = new[] { workingServer.WebUrl },
                Conventions = new DocumentConventions
                { DisableTopologyUpdates = true }
            }.Initialize())
            {
                Topology topo;
                using (var context = JsonOperationContext.ShortTermSingleUse())
                {
                    var value = await WaitForValueAsync(() =>
                    {
                        var topologyGetCommand = new GetDatabaseTopologyCommand();
                        tempStore.GetRequestExecutor().Execute(topologyGetCommand, context);
                        topo = topologyGetCommand.Result;
                        int rehab = 0;
                        int members = 0;
                        topo.Nodes.ForEach(n =>
                        {
                            switch (n.ServerRole)
                            {
                                case ServerNode.Role.Rehab:
                                    rehab++;
                                    break;
                                case ServerNode.Role.Member:
                                    members++;
                                    break;
                            }
                        });
                        return new Tuple<int, int>(rehab, members);

                    }, new Tuple<int, int>(rehabCount, memberCount));
                }
            }
        }

        public static async Task ReverseOrderSuccessfully(IDocumentStore store, string db)
        {
            var record = await store.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(db));
            record.Topology.Members.Reverse();
            var copy = new List<string>(record.Topology.Members);
            await store.Maintenance.Server.SendAsync(new ReorderDatabaseMembersOperation(db, record.Topology.Members));
            record = await store.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(db));
            Assert.True(copy.All(record.Topology.Members.Contains));
        }

        [Fact]
        public async Task EnsureCertificateReplacementDoesntOverConfirm_RavenDB_14791()
        {
            DebuggerAttachedTimeout.DisableLongTimespan = true;
            var clusterSize = 5;
            var (leader, nodes, serverCert) = await CreateLetsEncryptCluster(clusterSize);
            Assert.Equal(serverCert.Thumbprint, nodes[0].Certificate.Certificate.Thumbprint);
            var databaseName = GetDatabaseName();
            
            var options = new Options
            {
                Server = nodes[0],
                ReplicationFactor = clusterSize,
                ClientCertificate = serverCert,
                AdminCertificate = serverCert,
                ModifyDatabaseName = _ => databaseName,
                RunInMemory = true,
            };
            using (var store = GetDocumentStore(options))
            {
                string originalServerCert = nodes[0].Certificate.Certificate.Thumbprint;

                var requestExecutor = store.GetRequestExecutor(store.Database);
                
                //bring one of the other nodes down
                var serverToBringDown = Servers.First(x => x.ServerStore.IsLeader() == false);
                var (_, _, nodeDown) = await DisposeServerAndWaitForFinishOfDisposalAsync(serverToBringDown);
                
                //trigger cert refresh
                await requestExecutor.HttpClient.PostAsync(Uri.EscapeUriString($"{nodes[0].WebUrl}/admin/certificates/letsencrypt/force-renew"), null);
                
                var tries = 0;

                await WaitAndAssertForValueAsync(async () =>
                {
                    //simulate original problem where InstallUpdatedServerCertificateCommand happened lots of times and confirmations over-counted
                    foreach (var node in nodes)
                    {
                        if (node.ServerStore.NodeTag == nodeDown)
                            continue;
                        await node.ServerStore.InstallUpdatedCertificateValueChanged(0, nameof(InstallUpdatedServerCertificateCommand));
                    }

                    var res = await WaitForValueAsync( async () =>
                    {
                        var response = await requestExecutor.HttpClient.GetAsync(Uri.EscapeUriString($"{nodes[0].WebUrl}/admin/certificates/replacement/status"));
                        var rawResponse = response.Content.ReadAsStringAsync().Result;
                        var cert = JsonConvert.DeserializeObject<CertificateReplacement>(rawResponse);
                        
                        return cert?.Confirmations == clusterSize-1;
                    }, true, 3000, 500);

                    //count the tries to over confirm
                    if(res)
                        tries++;

                    //we try to over confirm 3 times
                    if (tries == 3 && res)
                        return true;

                    return false;
                }, true, 40_000, 4000);

                var response = await requestExecutor.HttpClient.GetAsync(Uri.EscapeUriString($"{nodes[0].WebUrl}/admin/certificates/replacement/status"));
                var rawResponse = response.Content.ReadAsStringAsync().Result;
                var cert = JsonConvert.DeserializeObject<CertificateReplacement>(rawResponse);
                var allActiveNodes = nodes[0].ServerStore.GetClusterTopology().AllNodes.Select(x => x.Key).ToHashSet();
                allActiveNodes.Remove(nodeDown);
                Assert.True(cert?.ConfirmedNodes.SetEquals(allActiveNodes));
                Assert.True(cert?.ReplacedNodes.Count() < clusterSize);

                foreach (var node in nodes)
                {
                    if (node.ServerStore.NodeTag == nodeDown)
                        continue;
                    
                    //all nodes should have old cert
                    Assert.Equal(originalServerCert, node.Certificate.Certificate.Thumbprint);
                }

            }
        }

        public async Task<(RavenServer Leader, List<RavenServer> Nodes, X509Certificate2 Cert)> CreateLetsEncryptCluster(int clutserSize)
        {
            var settingPath = Path.Combine(NewDataPath(forceCreateDir: true), "settings.json");
            var defaultSettingsPath = new PathSetting("settings.default.json").FullPath;
            File.Copy(defaultSettingsPath, settingPath, true);

            UseNewLocalServer(customConfigPath: settingPath);
            
            var acmeStaging = "https://acme-staging-v02.api.letsencrypt.org/";

            Server.Configuration.Core.AcmeUrl = acmeStaging;
            Server.ServerStore.Configuration.Core.SetupMode = SetupMode.Initial;

            var domain = "RavenClusterTest" + Environment.MachineName.Replace("-", "");
            string email;
            string rootDomain;

            Server.ServerStore.EnsureNotPassive();
            var license = Server.ServerStore.LoadLicense();

            using (var store = GetDocumentStore())
            using (var commands = store.Commands())
            using (Server.ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                var command = new AuthenticationLetsEncryptTests.ClaimDomainCommand(store.Conventions, context, new ClaimDomainInfo
                {
                    Domain = domain,
                    License = license
                });

                await commands.RequestExecutor.ExecuteAsync(command, commands.Context);

                Assert.True(command.Result.RootDomains.Length > 0);
                rootDomain = command.Result.RootDomains[0];
                email = command.Result.Email;
            }

            var nodeSetupInfos = new Dictionary<string, SetupInfo.NodeInfo>();
            char nodeTag = 'A';
            for (int i = 1; i <= clutserSize; i++)
            {
                var tcpListener = new TcpListener(IPAddress.Loopback, 0);
                tcpListener.Start();
                var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
                tcpListener.Stop();
                var setupNodeInfo = new SetupInfo.NodeInfo
                {
                    Port = port, 
                    Addresses = new List<string> {$"127.0.0.{i}"}
                };
                nodeSetupInfos.Add(nodeTag.ToString() ,setupNodeInfo);
                nodeTag++;
            }
            
            var setupInfo = new SetupInfo
            {
                Domain = domain,
                RootDomain = rootDomain,
                ModifyLocalServer = false,
                RegisterClientCert = false,
                Password = null,
                Certificate = null,
                LocalNodeTag = "A",
                License = license,
                Email = email,
                NodeSetupInfos = nodeSetupInfos
            };

            X509Certificate2 serverCert = default;
            byte[] serverCertBytes;
            BlittableJsonReaderObject settingsJsonObject;
            var customSettings = new List<IDictionary<string, string>>();

            using (var store = GetDocumentStore())
            using (var commands = store.Commands())
            using (Server.ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                var command = new AuthenticationLetsEncryptTests.SetupLetsEncryptCommand(store.Conventions, context, setupInfo);

                await commands.RequestExecutor.ExecuteAsync(command, commands.Context);

                Assert.True(command.Result.Length > 0);

                var zipBytes = command.Result;

                foreach(var node in setupInfo.NodeSetupInfos)
                {
                    try
                    {
                        var tag = node.Key;
                        settingsJsonObject = SetupManager.ExtractCertificatesAndSettingsJsonFromZip(zipBytes, tag, context, out serverCertBytes, out serverCert, out _, out _, out _, out _);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException("Unable to extract setup information from the zip file.", e);
                    }

                    settingsJsonObject.TryGet(RavenConfiguration.GetKey(x => x.Security.CertificatePassword), out string certPassword);
                    settingsJsonObject.TryGet(RavenConfiguration.GetKey(x => x.Security.CertificateLetsEncryptEmail), out string letsEncryptEmail);
                    settingsJsonObject.TryGet(RavenConfiguration.GetKey(x => x.Core.PublicServerUrl), out string publicServerUrl);
                    settingsJsonObject.TryGet(RavenConfiguration.GetKey(x => x.Core.ServerUrls), out string serverUrl);
                    settingsJsonObject.TryGet(RavenConfiguration.GetKey(x => x.Core.SetupMode), out SetupMode setupMode);
                    settingsJsonObject.TryGet(RavenConfiguration.GetKey(x => x.Core.ExternalIp), out string externalIp);

                    var tempFileName = GetTempFileName();
                    await File.WriteAllBytesAsync(tempFileName, serverCertBytes);
                    
                    var settings = new Dictionary<string, string>
                    {
                        [RavenConfiguration.GetKey(x => x.Security.CertificatePath)] = tempFileName,
                        [RavenConfiguration.GetKey(x => x.Security.CertificateLetsEncryptEmail)] = letsEncryptEmail,
                        [RavenConfiguration.GetKey(x => x.Security.CertificatePassword)] = certPassword,
                        [RavenConfiguration.GetKey(x => x.Core.PublicServerUrl)] = publicServerUrl,
                        [RavenConfiguration.GetKey(x => x.Core.ServerUrls)] = serverUrl,
                        [RavenConfiguration.GetKey(x => x.Core.SetupMode)] = setupMode.ToString(),
                        [RavenConfiguration.GetKey(x => x.Core.ExternalIp)] = externalIp,
                        [RavenConfiguration.GetKey(x => x.Core.AcmeUrl)] = acmeStaging
                    };
                    customSettings.Add(settings);
                }
            }

            Server.Dispose();
            
            var cluster = await CreateRaftClusterInternalAsync(clutserSize, customSettingsList: customSettings, leaderIndex: 0, useSsl: true);
            return (cluster.Leader, cluster.Nodes, serverCert);
        }
    }
}
