﻿using System;
using System.IO;

namespace Voron.Util.Settings
{
    public class VoronPathSetting : PathSettingBase<VoronPathSetting>
    {
        public VoronPathSetting(string path, string baseDataDir = null)
            : base(path, baseDataDir != null ? new VoronPathSetting(baseDataDir) : null)
        {
        }

        public override VoronPathSetting Combine(string path)
        {
            return new VoronPathSetting(Path.Combine(_path, path), _baseDataDir?.FullPath);
        }

        public override VoronPathSetting Combine(VoronPathSetting path)
        {
            return new VoronPathSetting(Path.Combine(_path, path._path), _baseDataDir?.FullPath);
        }

        public void ChangeExtention(string ext)
        {
            _path = $"{Path.GetFileNameWithoutExtension(_path)}.{ext}";
            _fullPath = $"{Path.GetFileNameWithoutExtension(_fullPath)}.{ext}";
        }
    }

    public class MemoryVoronPathSetting : VoronPathSetting
    {
        public MemoryVoronPathSetting() : base(":memory:")
        {
            _fullPath = _path;
        }
    }
}
