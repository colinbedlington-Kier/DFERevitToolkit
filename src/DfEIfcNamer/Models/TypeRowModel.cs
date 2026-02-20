namespace DfEIfcNamer.Models
{
    public class TypeRowModel : ViewModels.ViewModelBase
    {
        public int ElementId { get; set; }
        public string Category { get; set; }
        public string TypeName { get; set; }

        private string _ifcClassToken;
        public string IfcClassToken { get => _ifcClassToken; set { _ifcClassToken = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(PreviewIfcTypeName)); } }

        private string _predefinedType;
        public string PredefinedType { get => _predefinedType; set { _predefinedType = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(PreviewIfcTypeName)); } }

        private string _userDefinedValue;
        public string UserDefinedValue { get => _userDefinedValue; set { _userDefinedValue = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(PreviewIfcTypeName)); } }

        public int NextSequence { get; set; }

        public bool HasConflict { get; set; }

        public string PreviewIfcTypeName => $"{IfcClassToken}_{(PredefinedType == "USERDEFINED" ? UserDefinedValue : PredefinedType)}_Type{NextSequence:00}";
    }
}
