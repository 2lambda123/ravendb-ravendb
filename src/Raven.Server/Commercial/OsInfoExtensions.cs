﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;
using Raven.Client.ServerWide.Operations;
using Sparrow.Logging;
using Sparrow.Platform;

namespace Raven.Server.Commercial
{
    public static class OsInfoExtensions
    {
        public static OsInfo GetOsInfo(Logger logger)
        {
            try
            {
                OsInfo osInfo;
                if (PlatformDetails.RunningOnPosix == false)
                {
                    osInfo = GetWindowsOsInfo(logger);
                }
                else if (PlatformDetails.RunningOnMacOsx)
                {
                    osInfo = GetMacOsInfo();
                }
                else
                {
                    osInfo = GetLinuxOsInfo();
                }

                osInfo.Is32Bits = PlatformDetails.Is32Bits;
                return osInfo;
            }
            catch (Exception e)
            {
                if (logger.IsOperationsEnabled)
                    logger.Operations("Failed to get OS info", e);

                return null;
            }
        }

        private static OsInfo GetWindowsOsInfo(Logger logger)
        {
            var osInfo = GetDefaultWindowsOsInformation();

            try
            {
                var currentVersionSubKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");
                if (currentVersionSubKey == null)
                    return osInfo;

                var productName = currentVersionSubKey.GetValue("ProductName");
                if (productName != null)
                    osInfo.FullName = (string)productName;

                var majorVersion = currentVersionSubKey.GetValue("CurrentMajorVersionNumber");
                var minorVersion = currentVersionSubKey.GetValue("CurrentMinorVersionNumber");
                if (majorVersion != null && minorVersion != null)
                    osInfo.Version = $"{majorVersion}.{minorVersion}";

                var buildVersion = currentVersionSubKey.GetValue("CurrentBuildNumber") ??
                                   currentVersionSubKey.GetValue("CurrentBuild");

                if (buildVersion != null)
                    osInfo.BuildVersion = (string)buildVersion;

                var csdVersion = (string)currentVersionSubKey.GetValue("CSDVersion");
                if (csdVersion != null)
                {
                    if (csdVersion.Equals("Service Pack 1", StringComparison.OrdinalIgnoreCase))
                        csdVersion = "SP1";

                    osInfo.FullName += $" {csdVersion}";
                }

                var releaseId = (string)currentVersionSubKey.GetValue("ReleaseId");
                if (osInfo.Version == "10.0" && releaseId != null)
                {
                    osInfo.BuildVersion += $" ({releaseId})";
                }

                return osInfo;
            }
            catch (Exception e)
            {
                if (logger.IsOperationsEnabled)
                    logger.Operations("Failed to get Windows OS info from registry", e);

                return osInfo;
            }
        }

        private static OsInfo GetDefaultWindowsOsInformation()
        {
            var osInfo = new OsInfo
            {
                FullName = RuntimeInformation.OSDescription
            };

            try
            {
                const string winString = "Windows ";
                var idx = osInfo.FullName.IndexOf("Windows ", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return osInfo;

                var ver = osInfo.FullName.Substring(idx + winString.Length);
                if (ver == null)
                    return osInfo;

                var regex = new Regex(@"([0-9]+.[0-9]+)");
                var result = regex.Matches(ver);
                if (result.Count != 2)
                    return osInfo;

                osInfo.Version = result[0].Value;
                osInfo.BuildVersion = result[1].Value;
                
                if (decimal.TryParse(osInfo.Version, out var version) == false)
                    return osInfo;

                var isWindowsServer = IsOS(OS_ANYSERVER);
                switch (version)
                {
                    case 10.0m:
                        osInfo.FullName = isWindowsServer ? "Windows Server 2016" : "Windows 10";
                        break;
                    case 6.3m:
                        osInfo.FullName = isWindowsServer ? "Windows Server 2012 R2" : "Windows 8.1";
                        break;
                    case 6.2m:
                        osInfo.FullName = isWindowsServer ? "Windows Server 2012" : "Windows 8";
                        break;
                    case 6.1m:
                        osInfo.FullName = isWindowsServer ? "Windows Server 2008" : "Windows 7";
                        break;
                }

                return osInfo;
            }
            catch (Exception)
            {
                return osInfo;
            }
        }

        const int OS_ANYSERVER = 29;

        [DllImport("shlwapi.dll", SetLastError = true, EntryPoint = "#437")]
        private static extern bool IsOS(int os);

        private static OsInfo GetMacOsInfo()
        {
            try
            {
                var doc = new XmlDocument();
                const string systemVersionPlist = "/System/Library/CoreServices/SystemVersion.plist";
                doc.Load(systemVersionPlist);
                var dictionaryNode = doc.DocumentElement.SelectSingleNode("dict");

                Debug.Assert(dictionaryNode != null && dictionaryNode.ChildNodes.Count % 2 == 0);

                var osInfo = new OsInfo();
                for (var i = 0; i < dictionaryNode.ChildNodes.Count; i += 2)
                {
                    var keyNode = dictionaryNode.ChildNodes[i];
                    var valueNode = dictionaryNode.ChildNodes[i + 1];

                    switch (keyNode.InnerText)
                    {
                        case "ProductBuildVersion":
                            osInfo.BuildVersion = valueNode.InnerText;
                            break;
                        case "ProductVersion":
                            osInfo.Version = valueNode.InnerText;
                            break;
                    }
                }

                osInfo.FullName = GetMacOsName(osInfo.Version);

                return osInfo;
            }
            catch (Exception)
            {
                return new OsInfo
                {
                    FullName = RuntimeInformation.OSDescription
                };
            }
        }

        private static string GetMacOsName(string osInfoVersion)
        {
            const string macOSName = "macOS";

            if (osInfoVersion.StartsWith("10.14"))
                return $"{macOSName} Mojave";

            if (osInfoVersion.StartsWith("10.13"))
                return $"{macOSName} High Sierra";

            if (osInfoVersion.StartsWith("10.12"))
                return $"{macOSName} Sierra";

            if (osInfoVersion.StartsWith("10.11"))
                return $"{macOSName} El Capitan";

            return $"{macOSName} {osInfoVersion}";
        }

        private static OsInfo GetLinuxOsInfo()
        {
            var osInfo = new OsInfo
            {
                FullName = RuntimeInformation.OSDescription,
                BuildVersion = ReadBuildVersion()
            };

            try
            {
                var path = osInfo.FullName.Contains("SUSE", StringComparison.OrdinalIgnoreCase) 
                    ? "/usr/lib/os-release" 
                    : "/etc/os-release";

                var osReleaseProperties =
                    (from line in File.ReadAllLines(path)
                        let splitted = line.Split("=")
                        where splitted.Length == 2
                        select (Key: splitted[0], Value: splitted[1]))
                    .ToDictionary(x => x.Key, x => x.Value);

                osReleaseProperties.TryGetValue("NAME", out var name);

                var version = GetVersionByDistro(name);
                if (string.IsNullOrWhiteSpace(version) &&
                    osReleaseProperties.TryGetValue("VERSION_ID", out var versionId))
                {
                    version = versionId;
                }

                if (osReleaseProperties.TryGetValue("PRETTY_NAME", out var prettyName))
                {
                    osInfo.FullName = prettyName;
                }
                else if (string.IsNullOrWhiteSpace(version) == false && name != null)
                {
                    osInfo.FullName = $"{name} {version}";
                }

                osInfo.Version = version;

                return osInfo;
            }
            catch (Exception)
            {
                return osInfo;
            }
        }

        private static string GetVersionByDistro(string name)
        {
            if (name == null)
                return null;

            string version = null;
            if (name.Contains("CentOS", StringComparison.OrdinalIgnoreCase))
            {
                version = ReadAllTextWithoutError("/etc/centos-release") ??
                          ReadAllTextWithoutError("/etc/redhat-release") ??
                          ReadAllTextWithoutError("/etc/system-release");
            }
            else if (name.Contains("Debian", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Raspbian", StringComparison.OrdinalIgnoreCase))
            {
                version = ReadAllTextWithoutError("/etc/debian_version");
            }
            else if (name.Contains("Redhat", StringComparison.OrdinalIgnoreCase))
            {
                version = ReadAllTextWithoutError("/etc/redhat-release") ??
                          ReadAllTextWithoutError("/etc/system-release");
            }
            else if (name.Contains("Alpine", StringComparison.OrdinalIgnoreCase))
            {
                version = ReadAllTextWithoutError("/etc/alpine-release");
            }

            version = version ?? ReadAllTextWithoutError("/etc/lsb-release");

            if (string.IsNullOrWhiteSpace(version) == false)
            {
                var pattern = new Regex(@"\d+(\.\d+)+");
                var matches = pattern.Matches(version);
                version = matches
                    .Select(x => x.Value)
                    .OrderByDescending(x => x.Length)
                    .FirstOrDefault();
            }

            return version;
        }

        private static string ReadAllTextWithoutError(string path)
        {
            try
            {
                return File.ReadAllText(path);;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ReadBuildVersion()
        {
            try
            {
                return File.ReadLines("/proc/sys/kernel/osrelease").FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
