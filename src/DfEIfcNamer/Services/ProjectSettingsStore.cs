using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DfEIfcNamer.Services
{
    public class ProjectSettingsStore
    {
        private static readonly Guid SchemaGuid = new Guid("7D8A1BD5-75E0-4FA8-9680-C77811B75A3E");
        private const string SchemaName = "DfEIfcNamerCobieMapping";

        public MappingSettings Load(Document doc)
        {
            try
            {
               var schema = Schema.Lookup(SchemaGuid);

					Entity entity = new Entity();
					if (schema != null)
					{
						entity = doc.ProjectInformation.GetEntity(schema);
					}

					if (entity.IsValid())
                {
                    return new MappingSettings
                    {
                        InstanceSource = string.IsNullOrWhiteSpace(entity.Get<string>("InstanceSource")) ? "IFCName" : entity.Get<string>("InstanceSource"),
                        TypeSource = string.IsNullOrWhiteSpace(entity.Get<string>("TypeSource")) ? "IFCName [Type]" : entity.Get<string>("TypeSource"),
                        InstanceTarget = entity.Get<string>("InstanceTarget"),
                        TypeTarget = entity.Get<string>("TypeTarget"),
                        Scope = (SyncScope)entity.Get<int>("Scope"),
                        OverwriteMode = (OverwriteMode)entity.Get<int>("OverwriteMode"),
                        CategoryIds = entity.Get<IList<long>>("CategoryIds").ToList(),
                        LastSyncUtc = entity.Get<string>("LastSyncUtc") is string v && DateTime.TryParse(v, out var dt) ? dt : (DateTime?)null
                    };
                }
            }
            catch
            {
                // fall through to file.
            }

            return LoadFromFile(doc.Title);
        }

        public void Save(Document doc, MappingSettings settings)
        {
            try
            {
                var schema = EnsureSchema();
                var entity = new Entity(schema);
                entity.Set("InstanceSource", settings.InstanceSource ?? "IFCName");
                entity.Set("TypeSource", settings.TypeSource ?? "IFCName [Type]");
                entity.Set("InstanceTarget", settings.InstanceTarget ?? string.Empty);
                entity.Set("TypeTarget", settings.TypeTarget ?? string.Empty);
                entity.Set("Scope", (int)settings.Scope);
                entity.Set("OverwriteMode", (int)settings.OverwriteMode);
                entity.Set("CategoryIds", settings.CategoryIds ?? new List<long>());
                entity.Set("LastSyncUtc", settings.LastSyncUtc?.ToString("o") ?? string.Empty);
                doc.ProjectInformation.SetEntity(entity);
                return;
            }
            catch
            {
                SaveToFile(doc.Title, settings);
            }
        }

        private static Schema EnsureSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.AddSimpleField("InstanceSource", typeof(string));
            builder.AddSimpleField("TypeSource", typeof(string));
            builder.AddSimpleField("InstanceTarget", typeof(string));
            builder.AddSimpleField("TypeTarget", typeof(string));
            builder.AddSimpleField("Scope", typeof(int));
            builder.AddSimpleField("OverwriteMode", typeof(int));
            builder.AddArrayField("CategoryIds", typeof(long));
            builder.AddSimpleField("LastSyncUtc", typeof(string));
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            return builder.Finish();
        }

        private static string GetFallbackPath(string projectName)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "DfEIfcNamer");
            Directory.CreateDirectory(folder);
            var safe = string.Join("_", projectName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(folder, safe + ".mapping.json");
        }

        private static void SaveToFile(string projectName, MappingSettings settings)
        {
            var snapshot = MappingSettingsSnapshot.From(settings);
            var path = GetFallbackPath(projectName);
            using (var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var serializer = new DataContractJsonSerializer(typeof(MappingSettingsSnapshot));
                serializer.WriteObject(stream, snapshot);
            }
        }

        private static MappingSettings LoadFromFile(string projectName)
        {
            var path = GetFallbackPath(projectName);
            if (!File.Exists(path))
            {
                return new MappingSettings();
            }

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(MappingSettingsSnapshot));
                    var snapshot = serializer.ReadObject(stream) as MappingSettingsSnapshot;
                    return snapshot?.ToMappingSettings() ?? new MappingSettings();
                }
            }
            catch
            {
                return new MappingSettings();
            }
        }

        [DataContract]
        private class MappingSettingsSnapshot
        {
            [DataMember] public string InstanceSource { get; set; }
            [DataMember] public string TypeSource { get; set; }
            [DataMember] public string InstanceTarget { get; set; }
            [DataMember] public string TypeTarget { get; set; }
            [DataMember] public int Scope { get; set; }
            [DataMember] public int OverwriteMode { get; set; }
            [DataMember] public List<long> CategoryIds { get; set; }
            [DataMember] public string LastSyncUtc { get; set; }

            public static MappingSettingsSnapshot From(MappingSettings settings)
            {
                return new MappingSettingsSnapshot
                {
                    InstanceSource = settings.InstanceSource ?? "IFCName",
                    TypeSource = settings.TypeSource ?? "IFCName [Type]",
                    InstanceTarget = settings.InstanceTarget ?? string.Empty,
                    TypeTarget = settings.TypeTarget ?? string.Empty,
                    Scope = (int)settings.Scope,
                    OverwriteMode = (int)settings.OverwriteMode,
                    CategoryIds = settings.CategoryIds ?? new List<long>(),
                    LastSyncUtc = settings.LastSyncUtc?.ToString("o") ?? string.Empty
                };
            }

            public MappingSettings ToMappingSettings()
            {
                return new MappingSettings
                {
                    InstanceSource = string.IsNullOrWhiteSpace(InstanceSource) ? "IFCName" : InstanceSource,
                    TypeSource = string.IsNullOrWhiteSpace(TypeSource) ? "IFCName [Type]" : TypeSource,
                    InstanceTarget = InstanceTarget,
                    TypeTarget = TypeTarget,
                    Scope = (SyncScope)Scope,
                    OverwriteMode = (OverwriteMode)OverwriteMode,
                    CategoryIds = CategoryIds ?? new List<long>(),
                    LastSyncUtc = DateTime.TryParse(LastSyncUtc, out var dt) ? dt : (DateTime?)null
                };
            }
        }
    }
}
