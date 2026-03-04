using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using DfEIfcNamer.Models;

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
                var entity = schema == null ? Entity.Empty : doc.ProjectInformation.GetEntity(schema);
                if (entity.IsValid())
                {
                    return new MappingSettings
                    {
                        InstanceTarget = entity.Get<string>("InstanceTarget"),
                        TypeTarget = entity.Get<string>("TypeTarget"),
                        Scope = (SyncScope)entity.Get<int>("Scope"),
                        OverwriteMode = (OverwriteMode)entity.Get<int>("OverwriteMode"),
                        CategoryIds = entity.Get<IList<int>>("CategoryIds").ToList(),
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
                entity.Set("InstanceTarget", settings.InstanceTarget ?? string.Empty);
                entity.Set("TypeTarget", settings.TypeTarget ?? string.Empty);
                entity.Set("Scope", (int)settings.Scope);
                entity.Set("OverwriteMode", (int)settings.OverwriteMode);
                entity.Set("CategoryIds", settings.CategoryIds ?? new System.Collections.Generic.List<int>());
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
            builder.AddSimpleField("InstanceTarget", typeof(string));
            builder.AddSimpleField("TypeTarget", typeof(string));
            builder.AddSimpleField("Scope", typeof(int));
            builder.AddSimpleField("OverwriteMode", typeof(int));
            builder.AddArrayField("CategoryIds", typeof(int));
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
            return Path.Combine(folder, safe + ".mapping.txt");
        }

        private static void SaveToFile(string projectName, MappingSettings settings)
        {
            var line = string.Join("|",
                settings.InstanceTarget ?? string.Empty,
                settings.TypeTarget ?? string.Empty,
                (int)settings.Scope,
                (int)settings.OverwriteMode,
                string.Join(",", settings.CategoryIds ?? new System.Collections.Generic.List<int>()),
                settings.LastSyncUtc?.ToString("o") ?? string.Empty);
            File.WriteAllText(GetFallbackPath(projectName), line, Encoding.UTF8);
        }

        private static MappingSettings LoadFromFile(string projectName)
        {
            var path = GetFallbackPath(projectName);
            if (!File.Exists(path))
            {
                return new MappingSettings();
            }

            var parts = File.ReadAllText(path).Split('|');
            var result = new MappingSettings();
            if (parts.Length > 0) result.InstanceTarget = parts[0];
            if (parts.Length > 1) result.TypeTarget = parts[1];
            if (parts.Length > 2 && int.TryParse(parts[2], out var s)) result.Scope = (SyncScope)s;
            if (parts.Length > 3 && int.TryParse(parts[3], out var o)) result.OverwriteMode = (OverwriteMode)o;
            if (parts.Length > 4) result.CategoryIds = parts[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x)).ToList();
            if (parts.Length > 5 && DateTime.TryParse(parts[5], out var dt)) result.LastSyncUtc = dt;
            return result;
        }
    }
}
