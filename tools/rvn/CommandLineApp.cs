﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.CommandLineUtils;
using Raven.Server.Config;
using Sparrow.Platform;

namespace rvn
{
    internal static class CommandLineApp
    {
        private const string HelpOptionString = "-h | -? | --help";

        private const string EncryptionCommandsNote =
                "Setup encryption for the server store or decrypt an encrypted store. All commands MUST run under the same user as the one that the RavenDB server is using. The server MUST be offline for the duration of those operations";

        private static CommandLineApplication _app;

        public static int Run(string[] args)
        {
            if (args == null)
                throw new ArgumentNullException(nameof(args));

            _app = new CommandLineApplication();
            _app.Name = "rvn";
            _app.Description =
                @"This utility lets you manage RavenDB offline operations, such as setting encryption mode for the server store. The server store which may contain sensitive information is not encrypted by default (even if it contains encrypted databases). If you want it encrypted, you must do it manually using this tool.";

            _app.HelpOption(HelpOptionString);

            ConfigureOfflineOperationCommand();
            ConfigureAdminChannelCommand();
            ConfigureWindowsServiceCommand();

            _app.OnExecute(() =>
            {
                _app.ShowHelp();
                return 1;
            });

            try
            {
                return _app.Execute(args);
            }
            catch (CommandParsingException parsingException)
            {
                ExitWithError(parsingException.Message, _app);
                return 1;
            }
        }

        private static void ConfigureAdminChannelCommand()
        {
            _app.Command("admin-channel", cmd =>
            {
                cmd.Description = "Named Pipe Connection to RavenDB with PID. If PID ommited - will try auto pid discovery.";
                cmd.HelpOption(HelpOptionString);
                var pidArg = cmd.Argument("[pid]", "RavenDB Server process ID", cmdWithArg => { });
                cmd.OnExecute(() =>
                {
                    if (int.TryParse(pidArg.Value, out var pid))
                    {
                        AdminChannel.Connect(pid);
                    }
                    else
                    {
                        return ExitWithError("RavenDB server PID argument is mandatory.", cmd);
                    }

                    return 0;
                });
            });
        }

        private static int ExitWithError(string errMsg, CommandLineApplication cmd)
        {
            cmd.ShowHelp();
            cmd.Error.WriteLine(errMsg);
            return 1;
        }

        private static void ConfigureWindowsServiceCommand()
        {
            const string defaultServiceName = "RavenDB";

            if (PlatformDetails.RunningOnPosix)
            {
                return;
            }

            _app.Command("windows-service", cmd =>
            {
                cmd.Description = "allows to perform an operation on RavenDB Server run as Windows Service";
                cmd.HelpOption(HelpOptionString);
                ConfigureServiceNameOption(cmd);

                cmd.Command("register", subcmd =>
                {
                    var serviceNameOpt = ConfigureServiceNameOption(subcmd);
                    var serverDirOpt = subcmd.Option("--server-dir", "RavenDB Server directory", CommandOptionType.SingleValue);

                    subcmd.Description = "registers RavenDB Server as Windows Service";
                    subcmd.ExtendedHelpText = "\r\nRegisters RavenDB Server as Windows Service. Any additional arguments passed after command options are going to be passed to the server run as Windows Service.";
                    subcmd.HelpOption(HelpOptionString);

                    subcmd.OnExecute(() =>
                    {
                        WindowsService.Register(
                            serviceNameOpt.Value() ?? defaultServiceName, 
                            serverDirOpt.Value(), 
                            subcmd.RemainingArguments);

                        return 0;
                    });

                }, throwOnUnexpectedArg: false);

                cmd.Command("unregister", subcmd =>
                {
                    var serviceNameOpt = ConfigureServiceNameOption(subcmd);
                    subcmd.Description = "unregisters RavenDB Server Windows Service";
                    subcmd.HelpOption(HelpOptionString);

                    subcmd.OnExecute(() =>
                    {
                        WindowsService.Unregister(serviceNameOpt.Value() ?? defaultServiceName);
                        return 0;
                    });
                });

                cmd.Command("start", subcmd =>
                {
                    var serviceNameOpt = ConfigureServiceNameOption(subcmd);
                    subcmd.Description = "starts RavenDB Server Windows Service";
                    subcmd.HelpOption(HelpOptionString);

                    subcmd.OnExecute(() =>
                    {
                        WindowsService.Start(serviceNameOpt.Value() ?? defaultServiceName);
                        return 0;
                    });
                });

                cmd.Command("stop", subcmd =>
                {
                    var serviceNameOpt = ConfigureServiceNameOption(subcmd);
                    subcmd.Description = "stops RavenDB Server Windows Service";
                    subcmd.HelpOption(HelpOptionString);

                    subcmd.OnExecute(() =>
                    {
                        WindowsService.Stop(serviceNameOpt.Value() ?? defaultServiceName);
                        return 0;
                    });
                });

                cmd.OnExecute(() =>
                {
                    cmd.ShowHelp();
                    return 1;
                });
            });
        }

        private static CommandOption ConfigureServiceNameOption(CommandLineApplication cmd)
        {
            return cmd.Option("--service-name", "RavenDB Server Windows Service name", CommandOptionType.SingleValue);
        }

        private static void ConfigureOfflineOperationCommand()
        {
            _app.Command("offline-operation", cmd =>
            {
                cmd.Description = "Performs an offline operation on the RavenDB server.";
                cmd.HelpOption(HelpOptionString);

                cmd.Command("init-keys", subcmd =>
                {
                    subcmd.Description = "Generates encryption keys";
                    subcmd.OnExecute(() =>
                    {
                        OfflineOperations.InitKeys();
                        return 0;
                    });
                });

                cmd.Command("get-key", subcmd =>
                {
                    subcmd.Description = "exports unprotected server store encryption key to a given directory";
                    subcmd.ExtendedHelpText =
                        "\r\nExports unprotected server store encryption key to RavenDB directory. This key will allow decryption of the server store and must be secured. This is REQUIRED when restoring backups from an encrypted server store.";
                    subcmd.HelpOption(HelpOptionString);

                    var pathArg = subcmd.Argument("[path]", "RavenDB directory path");
                    subcmd.OnExecute(() =>
                    {
                        if (string.IsNullOrEmpty(pathArg.Value) == false)
                        {
                            OfflineOperations.GetKey(pathArg.Value);
                        }
                        else
                        {
                            return ExitWithError("RavenDB directory path argument is mandatory.", subcmd);
                        }

                        return 0;
                    });
                });

                cmd.Command("put-key", subcmd =>
                {
                    subcmd.Description = @"restores and protects the key for current OS user";
                    subcmd.HelpOption(HelpOptionString);
                    var path = subcmd.Argument("[path]", "RavenDB directory path");
                    subcmd.OnExecute(() =>
                    {
                        if (string.IsNullOrEmpty(path.Value) == false)
                        {
                            OfflineOperations.PutKey(path.Value);
                        }
                        else
                        {
                            return ExitWithError("RavenDB directory path argument is mandatory.", subcmd);
                        }

                        return 0;
                    });

                    subcmd.ExtendedHelpText =
                        "\r\nRestores the encryption key on the new machine and protects it for the current OS user. This is typically used as part of the restore process of an encrypted server store on a new machine";
                });

                cmd.Command("trust", subcmd =>
                {
                    subcmd.Description = "";

                    var keyArg = subcmd.Argument("[key]", "key");
                    var tagArg = subcmd.Argument("[tag]", "tag");

                    subcmd.OnExecute(() =>
                    {
                        if (subcmd.Arguments.Count == 2)
                        {
                            OfflineOperations.Trust(keyArg.Value, tagArg.Value);
                        }
                        else
                        {
                            return ExitWithError("Key and tag arguments are mandatory.", subcmd);
                        }

                        return 0;
                    });
                });

                cmd.Command("encrypt", subcmd =>
                {
                    subcmd.Description = "encrypts RavenDB files and saves the key to the same directory";
                    subcmd.ExtendedHelpText = $"\r\nEncrypts RavenDB files and saves the key to a given directory. This key file (secret.key.encrypted) is protected for the current OS user. Once encrypted, The server will only work for the current OS user. It is recommended that you do that as part of the initial setup of the server, before it is running. Encrypted server store can only talk to other encrypted server stores, and only over SSL.\r\n{ EncryptionCommandsNote }";
                    subcmd.HelpOption(HelpOptionString);
                    subcmd.Argument("[path]", "RavenDB directory path");
                    subcmd.OnExecute(() =>
                    {
                        var path = cmd.Arguments[0].Value;
                        if (string.IsNullOrEmpty(path) == false)
                        {
                            OfflineOperations.Encrypt(path);
                        }
                        else
                        {
                            return ExitWithError("RavenDB directory path argument is mandatory.", subcmd);
                        }

                        return 0;
                    });
                });

                cmd.Command("decrypt", subcmd =>
                {
                    subcmd.ExtendedHelpText = $"\r\nDecrypts RavenDB files in a given directory using the key inserted earlier using the put-key command.\r\n{ EncryptionCommandsNote }";
                    subcmd.Description = "descrypts RavenDB files";
                    subcmd.Argument("[path]", "RavenDB directory path");
                    subcmd.OnExecute(() =>
                    {
                        var path = cmd.Arguments[0].Value;
                        if (string.IsNullOrEmpty(path) == false)
                        {
                            OfflineOperations.Decrypt(path);
                        }
                        else
                        {
                            return ExitWithError("RavenDB directory path argument is mandatory.", subcmd);
                        }

                        return 0;
                    });
                });

                cmd.OnExecute(() =>
                {
                    _app.ShowHelp();
                    return 0;
                });
            });
        }
    }
}
