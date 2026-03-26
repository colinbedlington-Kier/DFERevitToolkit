using System.Collections.Generic;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public static class BuiltInAdsClassificationCatalog
    {
        public static IList<AdsClassificationEntry> Default()
        {
            return new List<AdsClassificationEntry>
            {
                new AdsClassificationEntry { Code = "ADS-TCH", Description = "Teaching Space" },
                new AdsClassificationEntry { Code = "ADS-SUP", Description = "Support Space" },
                new AdsClassificationEntry { Code = "ADS-SRV", Description = "Building Services Space" }
            };
        }
    }
}
