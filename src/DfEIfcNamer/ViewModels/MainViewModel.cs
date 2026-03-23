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
            ParameterResults = new ObservableCollection<ParameterBindingResult>();

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

            SetupStatus = "Ready.";
            CoverageStatus = "No setup diagnostics available.";

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
        public ObservableCollection<ParameterBindingResult> ParameterResults { get; }

        public string InstanceSource { get; set; }
        public string TypeSource { get; set; }
        public string InstanceTarget { get; set; }
        public string TypeTarget { get; set; }

        private string _selectedScope;
        public string SelectedScope
        {
            get => _selectedScope;
            set
            {
                _selectedScope = value;
                RaisePropertyChanged();
            }
        }

        private string _selectedOverwrite;
        public string SelectedOverwrite
        {
            get => _selectedOverwrite;
            set
            {
                _selectedOverwrite = value;
                RaisePropertyChanged();
            }
        }

        private string _setupStatus = "Ready.";
        public string SetupStatus
        {
            get => _setupStatus;
            set
            {
                _setupStatus = value;
                RaisePropertyChanged();
            }
        }

        private string _coverageStatus = "No setup diagnostics available.";
        public string CoverageStatus
        {
            get => _coverageStatus;
            set
            {
                _coverageStatus = value;
                RaisePropertyChanged();
            }
        }

        private string _documentStatus = "Document: n/a";
        public string DocumentStatus
        {
            get => _documentStatus;
            set
            {
                _documentStatus = value;
                RaisePropertyChanged();
            }
        }

        private string _mappingLoadedStatus = "Mapping loaded: no";
        public string MappingLoadedStatus
        {
            get => _mappingLoadedStatus;
            set
            {
                _mappingLoadedStatus = value;
                RaisePropertyChanged();
            }
        }

        private string _lastSyncStatus = "Last sync: never";
        public string LastSyncStatus
        {
            get => _lastSyncStatus;
            set
            {
                _lastSyncStatus = value;
                RaisePropertyChanged();
            }
        }

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
                        Categories.Add(new CategorySelectionItem
                        {
                            Id = category.Id.Value,
                            Name = category.Name,
                            IsSelected = true
                        });
                    }
                }
            });
        }

        private void CheckParameters()
        {
            SetupStatus = "Checking parameters...";
            Logs.Add("Checking shared parameter bindings...");

            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.CheckSetup,
                CategoryIds = GetSelectedCategoryIds(),
                Callback = r =>
                {
                    if (!string.IsNullOrWhiteSpace(r.Error))
                    {
                        SetupStatus = "⚠ " + NormalizeSetupMessage(r.Error, "Setup check failed.");
                        CoverageStatus = "No setup diagnostics available.";
                        ParameterResults.Clear();
                        Logs.Add("Check failed: " + r.Error);
                        return;
                    }

                    var status = r.SetupStatus;
                    ApplySetupStatus(status, isAssignRun: false);
                    Logs.Add("Check complete.");
                }
            });
        }

        private void AssignParameters()
        {
            SetupStatus = "Assigning parameters...";
            Logs.Add("Assigning shared parameters to the model...");

            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.AssignParameters,
                CategoryIds = GetSelectedCategoryIds(),
                Callback = r =>
                {
                    if (!string.IsNullOrWhiteSpace(r.Error))
                    {
                        SetupStatus = "⚠ " + NormalizeSetupMessage(r.Error, "Assign parameters failed.");
                        Logs.Add("Assign failed: " + r.Error);
                        return;
                    }

                    var status = r.SetupStatus;
                    ApplySetupStatus(status, isAssignRun: true);

                    if (status == null)
                    {
                        SetupStatus = "⚠ Assign parameters failed.";
                        Logs.Add("Assign returned no setup status.");
                        return;
                    }

                    if (status.ParameterResults != null && status.ParameterResults.Any(x => x.InsertSucceeded || x.ReInsertSucceeded))
                    {
                        SetupStatus = "Parameters assigned.";
                        Logs.Add("Assign complete: one or more parameters were inserted/reinserted.");
                    }
                    else if (status.ParameterResults != null && status.ParameterResults.Any())
                    {
                        SetupStatus = "⚠ Assign parameters failed.";
                        Logs.Add("Assign completed but no parameter bindings were inserted.");
                    }
                    else
                    {
                        SetupStatus = "⚠ Assign parameters failed.";
                        Logs.Add("Assign returned no parameter results.");
                    }
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
                    LastSyncStatus = settings.LastSyncUtc.HasValue
                        ? "Last sync: " + settings.LastSyncUtc.Value.ToLocalTime().ToString("g")
                        : "Last sync: never";
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
                OverwriteMode = SelectedOverwrite == "Overwrite always"
                    ? OverwriteMode.OverwriteAlways
                    : OverwriteMode.BlankOnly,
                CategoryIds = Categories.Where(c => c.IsSelected).Select(c => c.Id).ToList()
            };

            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.ApplySync,
                Settings = settings,
                Callback = r =>
                {
                    if (!string.IsNullOrWhiteSpace(r.Error))
                    {
                        Logs.Add("Error: " + r.Error);
                        return;
                    }

                    LastSyncStatus = "Last sync: " + DateTime.Now.ToString("g");

                    var syncResult = r.SyncResult;
                    if (syncResult == null)
                    {
                        Logs.Add("Sync completed, but no sync result was returned.");
                        return;
                    }

                    var summary =
                        $"Instances Updated/Skipped/Failed: {syncResult.InstancesUpdated}/{syncResult.InstancesSkipped}/{syncResult.InstancesFailed}; " +
                        $"Types Updated/Skipped/Failed: {syncResult.TypesUpdated}/{syncResult.TypesSkipped}/{syncResult.TypesFailed}";

                    Logs.Add(summary);

                    foreach (var log in syncResult.Logs ?? Enumerable.Empty<SyncLogEntry>())
                    {
                        Logs.Add($"{log.Severity}: {log.Message}");
                    }
                }
            });
        }

        private System.Collections.Generic.IList<ElementId> GetSelectedCategoryIds()
        {
            return Categories
                .Where(c => c.IsSelected)
                .Select(c => new ElementId(c.Id))
                .ToList();
        }

        private void ApplySetupStatus(SetupStatus status, bool isAssignRun)
        {
            CoverageStatus = BuildCoverageStatus(status);
            UpdateParameterResults(status);

            if (status == null)
            {
                SetupStatus = isAssignRun ? "⚠ Assign parameters failed." : "⚠ Setup check failed.";
                return;
            }

            var defaultMessage = isAssignRun ? "Assign parameters completed." : "Setup check completed.";
            SetupStatus = NormalizeSetupMessage(status.Message, defaultMessage);
        }

        private static string BuildCoverageStatus(SetupStatus status)
        {
            if (status == null)
            {
                return "No setup diagnostics available.";
            }

            return
                $"Shared parameter file loaded: {ToYesNo(status.SharedParameterFileFound)}\n" +
                $"Entity mapping JSON loaded: {ToYesNo(status.EntityMappingLoaded)}\n" +
                $"Classification slots JSON loaded: {ToYesNo(status.ClassificationSlotsLoaded)}\n" +
                $"Resolved add-in folder: {status.ResolvedAddinFolder}\n" +
                $"Shared parameter path: {status.SharedParameterFilePath}\n" +
                $"Included categories: {status.IncludedCategoriesCount}\n" +
                $"Skipped unsupported categories: {status.SkippedUnsupportedCategoriesCount}\n" +
                $"Binding failures: {status.FailedBindingInsertCount}";
        }

        private static string ToYesNo(bool value)
        {
            return value ? "yes" : "no";
        }

        private static string NormalizeSetupMessage(string message, string fallback)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return fallback;
            }

            if (message.IndexOf("readParamDatabase", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return fallback;
            }

            return message;
        }

        private void UpdateParameterResults(SetupStatus status)
        {
            ParameterResults.Clear();

            foreach (var result in status?.ParameterResults ?? Enumerable.Empty<ParameterBindingResult>())
            {
                ParameterResults.Add(result);
            }
        }
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
