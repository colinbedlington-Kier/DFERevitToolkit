using System.Collections.Generic;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public static class BuiltInZoneCatalog
    {
        public static IList<ZoneCatalogEntry> Default()
        {
            return new List<ZoneCatalogEntry>
            {
                new ZoneCatalogEntry { Name = "Teaching", Description = "General teaching accommodation", Category = "Curriculum", Hex = "#4F81BD", Rgb = "79,129,189" },
                new ZoneCatalogEntry { Name = "Administration", Description = "Administrative/support accommodation", Category = "Support", Hex = "#9BBB59", Rgb = "155,187,89" },
                new ZoneCatalogEntry { Name = "Circulation", Description = "Corridors/stairs/circulation", Category = "Core", Hex = "#C0504D", Rgb = "192,80,77" }
            };
        }
    }
}
