using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace DfEIfcNamer.Services
{
    public class ResourceFileLoader
    {
        private readonly Assembly _assembly;
        private readonly string _defaultNamespace;

        public ResourceFileLoader()
            : this(Assembly.GetExecutingAssembly())
        {
        }

        public ResourceFileLoader(Assembly assembly)
        {
            _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            _defaultNamespace = _assembly.GetName().Name ?? "DfEIfcNamer";
        }

        public string ResolveAddinFolder()
        {
            return Path.GetDirectoryName(_assembly.Location) ?? string.Empty;
        }

        public string ResolveExternalResourcePath(string fileName)
        {
            return Path.Combine(ResolveAddinFolder(), "Resources", fileName ?? string.Empty);
        }

        public string LoadTextResourceOrFile(string fileName, string explicitPath = null)
        {
            var loadedPath = ResolveExistingPath(fileName, explicitPath);
            if (!string.IsNullOrWhiteSpace(loadedPath))
            {
                return File.ReadAllText(loadedPath);
            }

            var embeddedName = ResolveEmbeddedResourceName(fileName);
            if (embeddedName == null)
            {
                throw BuildFileNotFound(fileName, explicitPath, ResolveExternalResourcePath(fileName), BuildCanonicalEmbeddedName(fileName));
            }

            using (var stream = _assembly.GetManifestResourceStream(embeddedName))
            {
                if (stream == null)
                {
                    throw BuildFileNotFound(fileName, explicitPath, ResolveExternalResourcePath(fileName), embeddedName);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public T LoadJsonResourceOrFile<T>(string fileName, string explicitPath = null, JsonSerializerOptions options = null)
        {
            return JsonSerializer.Deserialize<T>(LoadTextResourceOrFile(fileName, explicitPath), options ?? new JsonSerializerOptions())!;
        }

        public string[] LoadCsvResourceOrFile(string fileName, string explicitPath = null)
        {
            var csv = LoadTextResourceOrFile(fileName, explicitPath);
            return csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }

        public string ResolveEmbeddedResourceName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            var canonical = BuildCanonicalEmbeddedName(fileName);
            var names = _assembly.GetManifestResourceNames();
            return names.FirstOrDefault(n => string.Equals(n, canonical, StringComparison.Ordinal))
                ?? names.FirstOrDefault(n => string.Equals(n, canonical, StringComparison.OrdinalIgnoreCase));
        }

        private string BuildCanonicalEmbeddedName(string fileName)
        {
            return _defaultNamespace + ".Resources." + (fileName ?? string.Empty);
        }

        private string ResolveExistingPath(string fileName, string explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            {
                return explicitPath;
            }

            var external = ResolveExternalResourcePath(fileName);
            return File.Exists(external) ? external : null;
        }

        private static FileNotFoundException BuildFileNotFound(string fileName, string explicitPath, string externalPath, string embeddedName)
        {
            var message =
                "Default resource file could not be resolved." + Environment.NewLine +
                "Requested file: " + (fileName ?? "<null>") + Environment.NewLine +
                "Explicit path supplied: " + (string.IsNullOrWhiteSpace(explicitPath) ? "<none>" : explicitPath) + Environment.NewLine +
                "External path checked: " + externalPath + Environment.NewLine +
                "Embedded resource checked: " + embeddedName;

            return new FileNotFoundException(message, fileName);
        }
    }
}
