﻿using System;
using System.IO;
using System.Runtime.InteropServices;
using Sparrow.Platform;
using Voron.Platform.Posix;

namespace Voron.Util.Settings
{
    public abstract class PathSettingBase<T>
    {
        private readonly PathSettingBase<T> _baseDataDir;
        protected readonly string _path;

        protected string _fullPath;

        protected PathSettingBase(string path, PathSettingBase<T> baseDataDir = null)
        {
            _baseDataDir = baseDataDir;
            _path = path;
        }

        public string FullPath => _fullPath ?? (_fullPath = ToFullPath());

        public abstract T Combine(string path);

        public abstract T Combine(T path);

        public string ToFullPath()
        {
            var path = Environment.ExpandEnvironmentVariables(_path);

            if (PlatformDetails.RunningOnPosix == false && path.StartsWith(@"\") == false ||
                PlatformDetails.RunningOnPosix && path.StartsWith(@"/") == false) // if relative path
                path = Path.Combine(_baseDataDir?.FullPath ?? AppContext.BaseDirectory, path);

            var result = Path.IsPathRooted(path)
                ? path
                : Path.Combine(_baseDataDir?.FullPath ?? AppContext.BaseDirectory, path);

            if (result.Length >= 260 && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                result = @"\\?\" + result;

            if (result.EndsWith(@"\") || result.EndsWith(@"/"))
                result = result.TrimEnd('\\', '/');

            if (PlatformDetails.RunningOnPosix)
                return PosixHelper.FixLinuxPath(result);

            return Path.GetFullPath(result); // it will unify directory separators
        }

        public override string ToString()
        {
            return FullPath;
        }

        protected bool Equals(PathSettingBase<T> other)
        {
            return FullPath == other.FullPath;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((PathSettingBase<T>)obj);
        }

        public override int GetHashCode()
        {
            return FullPath.GetHashCode();
        }
    }
}
