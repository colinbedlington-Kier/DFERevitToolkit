using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DfEIfcNamer.Services
{
    public class SharedParameterFileInspector
    {
        public SharedParameterFileInspectionResult Inspect(string path, int previewLines = 20)
        {
            var result = new SharedParameterFileInspectionResult
            {
                FilePath = path,
                FileExists = File.Exists(path)
            };

            if (!result.FileExists)
            {
                return result;
            }

            try
            {
                var lines = File.ReadAllLines(path);
                result.IsReadable = true;
                result.FileLength = new FileInfo(path).Length;
                result.LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                result.PreviewLines = lines.Take(previewLines).ToList();

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("GROUP", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split('\t');
                        if (parts.Length >= 3)
                        {
                            result.Groups.Add(new ParsedGroupRecord { Id = parts[1], Name = parts[2] });
                        }
                    }
                    else if (trimmed.StartsWith("PARAM", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split('\t');
                        if (parts.Length >= 3)
                        {
                            result.Parameters.Add(new ParsedParameterRecord
                            {
                                Id = parts[1],
                                Name = parts[2],
                                GroupId = parts.Length >= 6 ? parts[5] : null,
                                DataType = parts.Length >= 4 ? parts[3] : null
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.ReadError = ex.Message;
            }

            return result;
        }
    }

    public class SharedParameterFileInspectionResult
    {
        public string FilePath { get; set; }
        public bool FileExists { get; set; }
        public bool IsReadable { get; set; }
        public long FileLength { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string ReadError { get; set; }
        public IList<string> PreviewLines { get; set; } = new List<string>();
        public IList<ParsedGroupRecord> Groups { get; set; } = new List<ParsedGroupRecord>();
        public IList<ParsedParameterRecord> Parameters { get; set; } = new List<ParsedParameterRecord>();
    }

    public class ParsedGroupRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class ParsedParameterRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string GroupId { get; set; }
        public string DataType { get; set; }
    }
}
