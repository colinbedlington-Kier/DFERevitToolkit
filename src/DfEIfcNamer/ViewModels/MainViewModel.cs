using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DfEIfcNamer.Commands;
using DfEIfcNamer.ExternalEvents;
using DfEIfcNamer.Models;
using DfEIfcNamer.Services;

namespace DfEIfcNamer.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;
        private readonly ResourceJsonService _resourceService;

        public MainViewModel(RevitRequestDispatcher dispatcher, ResourceJsonService resourceService, CounterStateService counterService)
        {
            _dispatcher = dispatcher;
            _resourceService = resourceService;

            var entities = _resourceService.LoadEntityLibrary();
            IfcEntities = new ObservableCollection<string>(entities.Select(x => x.IFCClassToken));
            PredefinedTypes = new ObservableCollection<string>(entities.SelectMany(x => x.PredefinedTypes).Distinct());
            Categories = new ObservableCollection<string> { "All" };
            InstanceScopes = new ObservableCollection<string> { "Selection", "View", "Model", "Category" };
            NumberingModes = new ObservableCollection<string> { "Sequential", "ElementId" };

            TypeRows = new ObservableCollection<TypeRowModel>();
            ProjectConfigJson = _resourceService.LoadDefaultProjectConfig();
            SelectedScope = "Model";
            SelectedNumberingMode = "Sequential";
            InstancePreview = "Preview: AIR-000001";

            ApplyTypesCommand = new RelayCommand(_ => DispatchTypes());
            ApplyInstancesCommand = new RelayCommand(_ => DispatchInstances());
            ExportIfcCommand = new RelayCommand(_ => _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.ExportIfc }));
            ExportAuditCommand = new RelayCommand(_ => _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.ExportAudit }));
            LoadConfigCommand = new RelayCommand(_ => ProjectConfigJson = _resourceService.LoadDefaultProjectConfig());
            SaveConfigCommand = new RelayCommand(_ => _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.SaveProjectConfig, JsonPayload = ProjectConfigJson }));
            ResetCountersCommand = new RelayCommand(_ => _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.ResetCounters }));

            _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.Bootstrap });
        }

        public ObservableCollection<string> Categories { get; }
        public ObservableCollection<string> IfcEntities { get; }
        public ObservableCollection<string> PredefinedTypes { get; }
        public ObservableCollection<TypeRowModel> TypeRows { get; }
        public ObservableCollection<string> InstanceScopes { get; }
        public ObservableCollection<string> NumberingModes { get; }

        public string SelectedCategory { get; set; }
        public string SelectedIfcEntity { get; set; }
        public string SelectedPredefinedType { get; set; }
        public string SearchText { get; set; }
        public string SelectedScope { get; set; }
        public string SelectedNumberingMode { get; set; }

        private string _instancePreview;
        public string InstancePreview { get => _instancePreview; set { _instancePreview = value; RaisePropertyChanged(); } }

        private string _projectConfigJson;
        public string ProjectConfigJson { get => _projectConfigJson; set { _projectConfigJson = value; RaisePropertyChanged(); } }

        public ICommand ApplyTypesCommand { get; }
        public ICommand ApplyInstancesCommand { get; }
        public ICommand ExportIfcCommand { get; }
        public ICommand ExportAuditCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand ResetCountersCommand { get; }

        private void DispatchTypes()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.ApplyTypeNames,
                TypeRows = TypeRows.ToList()
            });
        }

        private void DispatchInstances()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.ApplyInstanceNames,
                Scope = SelectedScope,
                NumberingMode = SelectedNumberingMode
            });
        }
    }
}
