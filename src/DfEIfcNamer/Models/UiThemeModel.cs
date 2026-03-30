namespace DfEIfcNamer.Models
{
    public class UiThemeModel
    {
        public string PrimaryFont { get; set; } = "Poppins";
        public string FallbackFont { get; set; } = "Arial";
        public string LogoPath { get; set; } = "pack://application:,,,/DfEIfcNamer;component/Resources/Brand/kier_logo.png";
        public string PrimaryColor { get; set; } = "#00263A";
        public string AccentColor { get; set; } = "#007B86";
        public string ErrorColor { get; set; } = "#DA242A";
        public string NeutralColor { get; set; } = "#3D4543";
    }
}
