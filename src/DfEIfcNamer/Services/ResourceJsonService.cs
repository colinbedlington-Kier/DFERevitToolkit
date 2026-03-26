using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class ResourceJsonService
    {
        private const string EntityFileName = "DfeIfc2x3Entities.json";
        private const string ClassificationFileName = "classification_slots.json";
        private readonly ResourceFileLoader _resourceLoader = new ResourceFileLoader();
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        public string ResolveAddinFolder() => _resourceLoader.ResolveAddinFolder();

        public string ResolveResourceFilePath(string fileName)
        {
            var resourceFolderPath = _resourceLoader.ResolveExternalResourcePath(fileName);
            if (File.Exists(resourceFolderPath))
            {
                return resourceFolderPath;
            }

            return Path.Combine(ResolveAddinFolder(), fileName);
        }

        public string ResolveEntityMappingPath() => ResolveResourceFilePath(EntityFileName);
        public string ResolveClassificationSlotsPath() => ResolveResourceFilePath(ClassificationFileName);

        public IList<IfcEntityDefinition> LoadEntityLibrary()
        {
            return _resourceLoader.LoadJsonResourceOrFile<List<IfcEntityDefinition>>(EntityFileName, ResolveEntityMappingPath(), JsonOpts)
                   ?? new List<IfcEntityDefinition>();
        }

        public IList<ClassificationSlot> LoadClassificationSlots()
        {
            return _resourceLoader.LoadJsonResourceOrFile<List<ClassificationSlot>>(ClassificationFileName, ResolveClassificationSlotsPath(), JsonOpts)
                   ?? new List<ClassificationSlot>();
        }

        public string LoadDefaultProjectConfig()
        {
            var entities = LoadEntityLibrary();
            return JsonSerializer.Serialize(new { entitiesCount = entities.Count, schema = "IFC2x3", version = 1 }, JsonOpts);
        }

        public void SaveProjectConfig(string json)
        {
            var path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "DfEIfcNamer", "project-config.json");
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, json ?? "{}");
        }
    }
}
