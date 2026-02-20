using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DfEIfcNamer.Utilities
{
    public static class NameFormatting
    {
        public static string ToPascalCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var words = Regex.Split(input, "[^A-Za-z0-9]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1).ToLowerInvariant());
            return string.Concat(words);
        }

        public static string NormalizePredefinedType(string predefinedType, string userDefined)
        {
            if (string.Equals(predefinedType, "USERDEFINED", System.StringComparison.OrdinalIgnoreCase))
            {
                return ToPascalCase(userDefined);
            }

            return ToPascalCase(predefinedType);
        }

        public static string SafeIfcToken(string token)
        {
            return ToPascalCase(token);
        }
    }
}
