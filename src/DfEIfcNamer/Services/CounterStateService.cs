using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;

namespace DfEIfcNamer.Services
{
    public class CounterStateService
    {
        private const string CounterParam = "DfE_NamingCounters";
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        public IDictionary<string, int> LoadCounters(Document doc)
        {
            var info = doc.ProjectInformation;
            var p = info.LookupParameter(CounterParam);
            if (p == null || string.IsNullOrWhiteSpace(p.AsString()))
            {
                return new Dictionary<string, int>();
            }

            return JsonSerializer.Deserialize<Dictionary<string, int>>(p.AsString(), JsonOpts) ?? new Dictionary<string, int>();
        }

        public void SaveCounters(Document doc, IDictionary<string, int> counters)
        {
            var info = doc.ProjectInformation;
            var p = info.LookupParameter(CounterParam);
            if (p != null && !p.IsReadOnly)
            {
                p.Set(JsonSerializer.Serialize(counters, JsonOpts));
            }
        }

        public void ResetCounters(Document doc)
        {
            using (var tg = new TransactionGroup(doc, "Reset DfE Counters"))
            {
                tg.Start();
                using (var tx = new Transaction(doc, "Reset DfE Counters"))
                {
                    tx.Start();
                    SaveCounters(doc, new Dictionary<string, int>());
                    tx.Commit();
                }
                tg.Assimilate();
            }
        }
    }
}
