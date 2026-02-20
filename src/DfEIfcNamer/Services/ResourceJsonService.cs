using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class ResourceJsonService
    {
        private const string EntityResource = "DfEIfcNamer.Resources.ifc2x3_entity_predefinedtypes.json";
        private const string ClassificationResource = "DfEIfcNamer.Resources.classification_slots.json";
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public IList<IfcEntityDefinition> LoadEntityLibrary()
        {
            var json = ReadEmbedded(EntityResource);
            return _serializer.Deserialize<List<IfcEntityDefinition>>(json) ?? new List<IfcEntityDefinition>();
        }

        public IList<ClassificationSlot> LoadClassificationSlots()
        {
            var json = ReadEmbedded(ClassificationResource);
            return _serializer.Deserialize<List<ClassificationSlot>>(json) ?? new List<ClassificationSlot>();
        }

        public string LoadDefaultProjectConfig()
        {
            var entities = LoadEntityLibrary();
            return _serializer.Serialize(new { entitiesCount = entities.Count, schema = "IFC2x3", version = 1 });
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

        private static string ReadEmbedded(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
