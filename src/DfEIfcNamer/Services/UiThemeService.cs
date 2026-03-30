using System;
using System.Text.Json;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.Services
{
    public class UiThemeService
    {
        private const string ThemeFileName = "Themes/kier_revit_ui_theme.json";
        private readonly ResourceFileLoader _loader = new ResourceFileLoader();

        public UiThemeModel LoadTheme()
        {
            try
            {
                var json = _loader.LoadTextResourceOrFile(ThemeFileName);
                var parsed = JsonSerializer.Deserialize<UiThemeModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return parsed ?? new UiThemeModel();
            }
            catch
            {
                return new UiThemeModel();
            }
        }
    }
}
