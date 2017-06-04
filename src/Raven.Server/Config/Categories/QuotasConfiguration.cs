﻿using System.ComponentModel;
using Raven.Server.Config.Attributes;

namespace Raven.Server.Config.Categories
{
    public class QuotasConfiguration : ConfigurationCategory
    {
        [DefaultValue(long.MaxValue)]
        [ConfigurationEntry("Raven/Quotas/Documents/HardLimit")]
        public long DocsHardLimit { get; set; }

        [DefaultValue(long.MaxValue)]
        [ConfigurationEntry("Raven/Quotas/Documents/SoftLimit")]
        public long DocsSoftLimit { get; set; }

        [Description("The hard limit after which we refuse any additional writes")]
        [DefaultValue(null)]
        [ConfigurationEntry("Raven/Quotas/Size/HardLimitInKB")]
        public string SizeHardLimit { get; set; }

        [Description("The soft limit before which we will warn about the quota")]
        [DefaultValue(null)]
        [ConfigurationEntry("Raven/Quotas/Size/SoftMarginInKB")]
        public string SizeSoftLimit { get; set; }
    }
}