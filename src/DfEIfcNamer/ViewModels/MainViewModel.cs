using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using DfEIfcNamer.Commands;
using DfEIfcNamer.ExternalEvents;
using DfEIfcNamer.Models;

namespace DfEIfcNamer.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;

        public MainViewModel(RevitRequestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;

            ScopeOptions = new ObservableCollection<string>(new[] { "Entire Model", "Active View" });
            OverwriteOptions = new ObservableCollection<string>(new[] { "Only write when target is blank", "Overwrite always" });
            InstanceTargetOptions = new ObservableCollection<string>();
            TypeTargetOptions = new ObservableCollection<string>();
            Categories = new ObservableCollection<CategorySelectionItem>();
            Logs = new ObservableCollection<string>();

            InstanceSource = "IFCName";
            TypeSource = "IFCName [Type]";
            InstanceTarget = "COBie.Component.Name";
            TypeTarget = "COBie.Type.Name";
            SelectedScope = ScopeOptions[0];
            SelectedOverwrite = OverwriteOptions[0];

            CheckParametersCommand = new RelayCommand(_ => CheckParameters());
            AssignParametersCommand = new RelayCommand(_ => AssignParameters());
            ReloadMappingCommand = new RelayCommand(_ => LoadMapping());
            SaveAndApplyCommand = new RelayCommand(_ => SaveAndApply());
            ExitCommand = new RelayCommand(_ => RequestClose?.Invoke());

            RefreshMetadata();
            LoadMapping();
        }

        public event Action RequestClose;

        public ObservableCollection<string> ScopeOptions { get; }
        public ObservableCollection<string> OverwriteOptions { get; }
        public ObservableCollection<string> InstanceTargetOptions { get; }
        public ObservableCollection<string> TypeTargetOptions { get; }
        public ObservableCollection<CategorySelectionItem> Categories { get; }
        public ObservableCollection<string> Logs { get; }

        public string InstanceSource { get; set; }
        public string TypeSource { get; set; }
        public string InstanceTarget { get; set; }
        public string TypeTarget { get; set; }

        private string _selectedScope;
        public string SelectedScope { get => _selectedScope; set { _selectedScope = value; RaisePropertyChanged(); } }

        private string _selectedOverwrite;
        public string SelectedOverwrite { get => _selectedOverwrite; set { _selectedOverwrite = value; RaisePropertyChanged(); } }

        private string _setupStatus = "Not checked";
        public string SetupStatus { get => _setupStatus; set { _setupStatus = value; RaisePropertyChanged(); } }

        private string _coverageStatus = "Unknown";
        public string CoverageStatus { get => _coverageStatus; set { _coverageStatus = value; RaisePropertyChanged(); } }

        private string _documentStatus = "Document: n/a";
        public string DocumentStatus { get => _documentStatus; set { _documentStatus = value; RaisePropertyChanged(); } }

        private string _mappingLoadedStatus = "Mapping loaded: no";
        public string MappingLoadedStatus { get => _mappingLoadedStatus; set { _mappingLoadedStatus = value; RaisePropertyChanged(); } }

        private string _lastSyncStatus = "Last sync: never";
        public string LastSyncStatus { get => _lastSyncStatus; set { _lastSyncStatus = value; RaisePropertyChanged(); } }

        public ICommand CheckParametersCommand { get; }
        public ICommand AssignParametersCommand { get; }
        public ICommand ReloadMappingCommand { get; }
        public ICommand SaveAndApplyCommand { get; }
        public ICommand ExitCommand { get; }

        private void RefreshMetadata()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.GetAvailableParameters,
                Callback = r =>
                {
                    InstanceTargetOptions.Clear();
                    foreach (var p in r.InstanceParameters ?? Enumerable.Empty<ProjectParameterOption>())
                    {
                        InstanceTargetOptions.Add(p.Name);
                    }

                    TypeTargetOptions.Clear();
                    foreach (var p in r.TypeParameters ?? Enumerable.Empty<ProjectParameterOption>())
                    {
                        TypeTargetOptions.Add(p.Name);
                    }
                }
            });

            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.GetCategories,
                Callback = r =>
                {
                    Categories.Clear();
                    foreach (var category in r.Categories ?? Enumerable.Empty<Category>())
                    {
                        Categories.Add(new CategorySelectionItem { Id = category.Id.Value, Name = category.Name, IsSelected = true });
                    }
                }
            });
        }

        private void CheckParameters()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.CheckSetup,
                CategoryIds = Categories.Where(c => c.IsSelected).Select(c => new ElementId(c.Id)).ToList(),
                Callback = r =>
                {
                    if (r.Error != null)
                    {
                        SetupStatus = "⚠ " + r.Error;
                        return;
                    }

                    var status = r.SetupStatus;
                    SetupStatus = status.SharedParameterFileFound && status.InstanceParameterBound && status.TypeParameterBound ? "✓ Parameters configured" : "⚠ Setup required";
                    CoverageStatus = BuildCoverageStatus(status);
                    Logs.Add($"Check: {status.Message}");
                }
            });
        }

        private void AssignParameters()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.AssignParameters,
                CategoryIds = Categories.Where(c => c.IsSelected).Select(c => new ElementId(c.Id)).ToList(),
                Callback = r =>
                {
                    if (r.Error != null)
                    {
                        SetupStatus = "⚠ " + r.Error;
                        return;
                    }

                    SetupStatus = "✓ Parameters assigned";
                    CoverageStatus = BuildCoverageStatus(r.SetupStatus);
                    Logs.Add("Assign Parameters executed.");
                }
            });
        }

        private void LoadMapping()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.LoadMapping,
                Callback = r =>
                {
                    var settings = r.Settings ?? new MappingSettings();
                    InstanceSource = string.IsNullOrWhiteSpace(settings.InstanceSource) ? "IFCName" : settings.InstanceSource;
                    TypeSource = string.IsNullOrWhiteSpace(settings.TypeSource) ? "IFCName [Type]" : settings.TypeSource;
                    InstanceTarget = string.IsNullOrWhiteSpace(settings.InstanceTarget) ? "COBie.Component.Name" : settings.InstanceTarget;
                    TypeTarget = string.IsNullOrWhiteSpace(settings.TypeTarget) ? "COBie.Type.Name" : settings.TypeTarget;
                    SelectedScope = settings.Scope == SyncScope.ActiveView ? ScopeOptions[1] : ScopeOptions[0];
                    SelectedOverwrite = settings.OverwriteMode == OverwriteMode.OverwriteAlways ? OverwriteOptions[1] : OverwriteOptions[0];
                    LastSyncStatus = settings.LastSyncUtc.HasValue ? "Last sync: " + settings.LastSyncUtc.Value.ToLocalTime().ToString("g") : "Last sync: never";
                    MappingLoadedStatus = "Mapping loaded: yes";
                    RaisePropertyChanged(nameof(InstanceSource));
                    RaisePropertyChanged(nameof(TypeSource));
                    RaisePropertyChanged(nameof(InstanceTarget));
                    RaisePropertyChanged(nameof(TypeTarget));
                }
            });
        }

        private void SaveAndApply()
        {
            Logs.Clear();
            var settings = new MappingSettings
            {
                InstanceSource = InstanceSource,
                TypeSource = TypeSource,
                InstanceTarget = InstanceTarget,
                TypeTarget = TypeTarget,
                Scope = SelectedScope == "Active View" ? SyncScope.ActiveView : SyncScope.EntireModel,
                OverwriteMode = SelectedOverwrite == "Overwrite always" ? OverwriteMode.OverwriteAlways : OverwriteMode.BlankOnly,
                CategoryIds = Categories.Where(c => c.IsSelected).Select(c => c.Id).ToList()
            };

            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.ApplySync,
                Settings = settings,
                Callback = r =>
                {
                    if (r.Error != null)
                    {
                        Logs.Add("Error: " + r.Error);
                        return;
                    }

                    LastSyncStatus = "Last sync: " + DateTime.Now.ToString("g");
                    var summary = $"Instances Updated/Skipped/Failed: {r.SyncResult.InstancesUpdated}/{r.SyncResult.InstancesSkipped}/{r.SyncResult.InstancesFailed}; " +
                                  $"Types Updated/Skipped/Failed: {r.SyncResult.TypesUpdated}/{r.SyncResult.TypesSkipped}/{r.SyncResult.TypesFailed}";
                    Logs.Add(summary);
                    foreach (var log in r.SyncResult.Logs)
                    {
                        Logs.Add($"{log.Severity}: {log.Message}");
                    }
                }
            });
        }

        private static string BuildCoverageStatus(SetupStatus status)
        {
            if (status == null)
            {
                return "No setup diagnostics available.";
            }

            return
                $"Resolved add-in folder: {status.ResolvedAddinFolder}\n" +
                $"Shared parameter file path: {status.SharedParameterFilePath}\n" +
                $"Shared parameter file exists: {YesNo(status.SharedParameterFileFound)}\n" +
                $"Entity mapping JSON path: {status.EntityMappingJsonPath}\n" +
                $"Entity mapping JSON exists: {YesNo(status.EntityMappingFileExists)}\n" +
                $"Entity mapping JSON loaded: {YesNo(status.EntityMappingLoaded)}\n" +
                $"Classification slots JSON path: {status.ClassificationSlotsJsonPath}\n" +
                $"Classification slots JSON exists: {YesNo(status.ClassificationSlotsFileExists)}\n" +
                $"Classification slots JSON loaded: {YesNo(status.ClassificationSlotsLoaded)}\n" +
                $"Included categories: {status.IncludedCategoriesCount}\n" +
                $"Skipped unsupported categories: {status.SkippedUnsupportedCategoriesCount}\n" +
                $"Binding failures: {status.FailedBindingInsertCount}\n" +
                $"Error details: {string.IsNullOrWhiteSpace(status.ErrorDetails) ? \"None\" : status.ErrorDetails}";
        }

        private static string YesNo(bool value) => value ? "yes" : "no";
    }

    public class CategorySelectionItem : ViewModelBase
    {
        public long Id { get; set; }
        public string Name { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                RaisePropertyChanged();
            }
        }
    }
}
