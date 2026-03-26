using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class ResourceJsonService
    {
        private const string EntityResource = "DfEIfcNamer.Resources.DfeIfc2x3Entities.json";
        private const string ClassificationResource = "DfEIfcNamer.Resources.classification_slots.json";
        private const string EntityFileName = "DfeIfc2x3Entities.json";
        private const string ClassificationFileName = "classification_slots.json";
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        public string ResolveAddinFolder()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        }

        public string ResolveResourceFilePath(string fileName)
        {
            var addinFolder = ResolveAddinFolder();
            var resourceFolderPath = Path.Combine(addinFolder, "Resources", fileName);
            if (File.Exists(resourceFolderPath))
            {
                return resourceFolderPath;
            }

            return Path.Combine(addinFolder, fileName);
        }

        public string ResolveEntityMappingPath() => ResolveResourceFilePath(EntityFileName);
        public string ResolveClassificationSlotsPath() => ResolveResourceFilePath(ClassificationFileName);

        public IList<IfcEntityDefinition> LoadEntityLibrary()
        {
            var json = ReadWithFallback(ResolveEntityMappingPath(), EntityResource);
            return JsonSerializer.Deserialize<List<IfcEntityDefinition>>(json, JsonOpts) ?? new List<IfcEntityDefinition>();
        }

        public IList<ClassificationSlot> LoadClassificationSlots()
        {
            var json = ReadWithFallback(ResolveClassificationSlotsPath(), ClassificationResource);
            return JsonSerializer.Deserialize<List<ClassificationSlot>>(json, JsonOpts) ?? new List<ClassificationSlot>();
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

        private static string ReadWithFallback(string diskPath, string embeddedName)
        {
            if (File.Exists(diskPath))
            {
                return File.ReadAllText(diskPath);
            }

            return ReadEmbedded(embeddedName);
        }

        private static string ReadEmbedded(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Embedded resource not found: " + name);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
