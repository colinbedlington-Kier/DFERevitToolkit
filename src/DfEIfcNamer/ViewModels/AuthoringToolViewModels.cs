using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows;
using System.Windows.Data;
using Autodesk.Revit.DB;
using DfEIfcNamer.Commands;
using DfEIfcNamer.ExternalEvents;
using DfEIfcNamer.Models;
using DfEIfcNamer.Services;
using Microsoft.Win32;

namespace DfEIfcNamer.ViewModels
{
    public class AuthoringToolViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;

        public AuthoringToolViewModel(RevitRequestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            Setup = new SetupViewModel(dispatcher);
            Naming = new NamingViewModel(dispatcher);
            SystemAssignment = new SystemAssignmentViewModel(dispatcher, Naming);
            HeaderData = new HeaderDataViewModel(dispatcher);
            SpaceZone = new SpaceZoneViewModel(dispatcher);
            ClassificationSync = new ClassificationSyncViewModel(dispatcher);
            Validation = new ValidationViewModel(dispatcher, Setup, Naming, HeaderData, SpaceZone);
            ComplianceValidation = new ComplianceValidationViewModel(dispatcher);
            ExitCommand = new RelayCommand(_ => RequestClose?.Invoke());
            DocumentStatus = "Document: n/a";
        }

        public event Action RequestClose;
        public SetupViewModel Setup { get; }
        public NamingViewModel Naming { get; }
        public SystemAssignmentViewModel SystemAssignment { get; }
        public HeaderDataViewModel HeaderData { get; }
        public SpaceZoneViewModel SpaceZone { get; }
        public ClassificationSyncViewModel ClassificationSync { get; }
        public ValidationViewModel Validation { get; }
        public ComplianceValidationViewModel ComplianceValidation { get; }
        public ICommand ExitCommand { get; }

        private string _documentStatus;
        public string DocumentStatus { get => _documentStatus; set { _documentStatus = value; RaisePropertyChanged(); } }

        private string _globalStatus = "Ready";
        public string GlobalStatus { get => _globalStatus; set { _globalStatus = value; RaisePropertyChanged(); } }

        public void RefreshDocumentStatus(string title) => DocumentStatus = "Document: " + (title ?? "n/a");
    }

    public class SetupViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;
        private readonly UiThemeService _themeService = new UiThemeService();
        private readonly ResourceFileLoader _resourceLoader = new ResourceFileLoader();
        public SetupViewModel(RevitRequestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            ParameterStatuses = new ObservableCollection<RequiredParameterStatus>();
            Log = new ObservableCollection<string>();
            WarningLog = new ObservableCollection<string>();
            ErrorLog = new ObservableCollection<string>();
            Categories = new ObservableCollection<CategorySelectionItem>();
            CheckSetupCommand = new RelayCommand(_ => CheckSetup());
            CreateMissingParametersCommand = new RelayCommand(_ => CreateParameters());
            LoadNamingCodeMapCommand = new RelayCommand(_ => LoadNamingCodes());
            LoadSystemListCommand = new RelayCommand(_ => LoadSystems());
            ExportSetupReportCommand = new RelayCommand(_ => ExportReport());
            CopyDebugReportCommand = new RelayCommand(_ => CopyDebugReport());
            Status = "Not checked";
            var theme = _themeService.LoadTheme();
            DebugLines.Add("Theme config: " + (string.IsNullOrWhiteSpace(theme?.PrimaryColor) ? "unavailable" : "loaded"));
            var logoResource = _resourceLoader.ResolveEmbeddedResourceName("Brand/kier_logo.png");
            DebugLines.Add("Logo resource: " + (string.IsNullOrWhiteSpace(logoResource) ? "missing" : "resolved"));
            LoadCategories();
        }

        public ObservableCollection<RequiredParameterStatus> ParameterStatuses { get; }
        public ObservableCollection<string> Log { get; }
        public ObservableCollection<string> WarningLog { get; }
        public ObservableCollection<string> ErrorLog { get; }
        public ObservableCollection<CategorySelectionItem> Categories { get; }
        public ICommand CheckSetupCommand { get; }
        public ICommand CreateMissingParametersCommand { get; }
        public ICommand LoadNamingCodeMapCommand { get; }
        public ICommand LoadSystemListCommand { get; }
        public ICommand ExportSetupReportCommand { get; }
        public ICommand CopyDebugReportCommand { get; }
        public SetupCheckResult LastResult { get; private set; }

        private string _status;
        public string Status { get => _status; set { _status = value; RaisePropertyChanged(); } }

        private string _namingCodeStatus = "Naming map: embedded";
        public string NamingCodeStatus { get => _namingCodeStatus; set { _namingCodeStatus = value; RaisePropertyChanged(); } }

        private string _systemListStatus = "System list: embedded";
        public string SystemListStatus { get => _systemListStatus; set { _systemListStatus = value; RaisePropertyChanged(); } }
        private string _debugStatus = "Debug: awaiting setup check.";
        public string DebugStatus { get => _debugStatus; set { _debugStatus = value; RaisePropertyChanged(); } }
        public ObservableCollection<string> DebugLines { get; } = new ObservableCollection<string>();

        private void LoadCategories()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.GetCategories,
                Callback = r =>
                {
                    Categories.Clear();
                    foreach (var c in r.Categories ?? Enumerable.Empty<Category>()) Categories.Add(new CategorySelectionItem { Id = c.Id.Value, Name = c.Name, IsSelected = true });
                }
            });
        }

        private void CheckSetup()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.CheckAuthoringSetup,
                CategoryIds = Categories.Where(c => c.IsSelected).Select(c => new ElementId(c.Id)).ToList(),
                Callback = r =>
                {
                    if (!string.IsNullOrWhiteSpace(r.Error))
                    {
                        Log.Add("Setup check error: " + r.Error);
                    }

                    ApplyResult(r.AuthoringSetup ?? new SetupCheckResult { Status = "Error", Notes = r.Error ?? "Unknown setup error." });
                }
            });
        }

        private void CreateParameters()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.CreateAuthoringParameters,
                CategoryIds = Categories.Where(c => c.IsSelected).Select(c => new ElementId(c.Id)).ToList(),
                Callback = r =>
                {
                    if (!string.IsNullOrWhiteSpace(r.Error))
                    {
                        Log.Add("Create parameters error: " + r.Error);
                    }

                    ApplyResult(r.AuthoringSetup ?? new SetupCheckResult { Status = "Error", Notes = r.Error ?? "Unknown create error." });
                }
            });
        }

        private void LoadNamingCodes()
        {
            var dialog = new OpenFileDialog { Filter = "JSON/CSV|*.json;*.csv" };
            var path = dialog.ShowDialog() == true ? dialog.FileName : null;
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.LoadNamingCodeMap,
                ExternalPath = path,
                Callback = r =>
                {
                    NamingCodeStatus = $"Naming map entries: {r.NamingCodes?.Count ?? 0}";
                    if (!string.IsNullOrWhiteSpace(r.Error)) Log.Add("Error loading naming map: " + r.Error);
                    Log.Add("Loaded naming code map.");
                }
            });
        }

        private void LoadSystems()
        {
            var dialog = new OpenFileDialog { Filter = "JSON/CSV|*.json;*.csv" };
            var path = dialog.ShowDialog() == true ? dialog.FileName : null;
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.LoadSystemList,
                ExternalPath = path,
                Callback = r =>
                {
                    var loaded = r.Systems?.Count ?? 0;
                    SystemListStatus = $"Systems loaded: {loaded}, bound to UI: {loaded}, filtered out: 0";
                    if (!string.IsNullOrWhiteSpace(r.Error)) Log.Add("Error loading systems: " + r.Error);
                    Log.Add("Loaded system list.");
                }
            });
        }

        private void ExportReport()
        {
            var dialog = new SaveFileDialog { Filter = "Text|*.txt", FileName = "DfE_SetupReport.txt" };
            if (dialog.ShowDialog() != true) return;
            var sb = new StringBuilder();
            sb.AppendLine(Status);
            foreach (var p in ParameterStatuses) sb.AppendLine($"{p.ParameterName}\t{p.Scope}\t{p.Exists}\t{p.Writable}\t{p.Notes}");
            File.WriteAllText(dialog.FileName, sb.ToString());
        }

        private void ApplyResult(SetupCheckResult setup)
        {
            LastResult = setup;
            Status = $"{setup?.Status ?? "Error"}: {setup?.Notes}";
            ParameterStatuses.Clear();
            foreach (var row in setup?.Parameters ?? Enumerable.Empty<RequiredParameterStatus>()) ParameterStatuses.Add(row);
            DebugLines.Clear();
            WarningLog.Clear();
            ErrorLog.Clear();
            DebugLines.Add($"Manifest loaded: {(setup?.ManifestLoaded == true ? "Yes" : "No")} ({setup?.ManifestTotalRowsCount ?? 0} rows)");
            DebugLines.Add($"Manifest parsed: {setup?.ManifestParsedRowsCount ?? 0} OK, {setup?.ManifestFailedRowsCount ?? 0} failed");
            DebugLines.Add($"Manifest source: {setup?.ManifestSource ?? "unknown"}");
            DebugLines.Add($"Shared parameter loaded: {(setup?.SharedParameterFileLoaded == true ? "Y" : "N")} ({setup?.SharedParameterSource ?? "unknown"})");
            DebugLines.Add($"Naming map source: {setup?.NamingCodesSource ?? "n/a"}");
            DebugLines.Add($"Systems source: {setup?.SystemsSource ?? "n/a"}");
            DebugLines.Add($"Manifest entries mapped: {setup?.ManifestEntriesCount ?? 0}");
            DebugLines.Add($"Shared definitions parsed: {setup?.SharedParameterDefinitionsCount ?? 0}");
            DebugLines.Add($"Manifest/shared matches: {setup?.MatchedSharedParameterDefinitionsCount ?? 0}");
            DebugLines.Add($"Projected setup rows: {setup?.ProjectedRowsCount ?? 0}");
            foreach (var ex in setup?.Exceptions ?? Enumerable.Empty<string>())
            {
                var line = "Exception: " + ex;
                DebugLines.Add(line);
                ErrorLog.Add(line);
            }
            foreach (var rowErr in setup?.RowLevelErrors ?? Enumerable.Empty<string>())
            {
                var line = "Row error: " + rowErr;
                DebugLines.Add(line);
                WarningLog.Add(line);
            }
            DebugStatus = DebugLines.Any(x => x.StartsWith("Exception:") || x.StartsWith("Row error:")) ? "Debug: failures detected." : "Debug: no setup exceptions detected.";
            if (!string.IsNullOrWhiteSpace(Status)) Log.Add(Status);
        }

        private void CopyDebugReport()
        {
            try
            {
                var report = string.Join(Environment.NewLine, DebugLines);
                Clipboard.SetText(report);
                Log.Add("Debug report copied to clipboard.");
            }
            catch (Exception ex)
            {
                Log.Add("Copy debug report failed: " + ex.Message);
            }
        }
    }

    public class NamingViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;
        public NamingViewModel(RevitRequestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            ScopeModes = new ObservableCollection<string>(Enum.GetNames(typeof(NamingScopeMode)));
            NumberingModes = new ObservableCollection<string>(Enum.GetNames(typeof(InstanceNumberingMode)));
            Rows = new ObservableCollection<NamingPreviewRow>();
            FilteredRows = new ObservableCollection<NamingPreviewRow>();
            Categories = new ObservableCollection<CategorySelectionItem>();
            Systems = new ObservableCollection<SystemRegistryEntry>();
            Warnings = new ObservableCollection<string>();
            GeneratePreviewCommand = new RelayCommand(_ => Generate());
            ApplyIfcNameCommand = new RelayCommand(_ => Apply(RevitRequestId.ApplyNamingInstance));
            ApplyIfcTypeCommand = new RelayCommand(_ => Apply(RevitRequestId.ApplyNamingType));
            ApplySystemDataCommand = new RelayCommand(_ => Apply(RevitRequestId.ApplySystemData));
            ApplyAllCommand = new RelayCommand(_ => Apply(RevitRequestId.ApplyNamingAll));
            ValidateCommand = new RelayCommand(_ => ValidateOnly());
            ExportReportCommand = new RelayCommand(_ => Export());
            ApplyFiltersCommand = new RelayCommand(_ => ApplyFilters());
            SelectAllRowsCommand = new RelayCommand(_ => SetSelection(true));
            UnselectAllRowsCommand = new RelayCommand(_ => SetSelection(false));
            FillDownPredefinedTypeCommand = new RelayCommand(_ => FillDownPredefinedType());
            RefreshCommand = new RelayCommand(_ => Generate());
            ScopeMode = ScopeModes.First();
            NumberingMode = NumberingModes.First();
            FallbackCode = "UNM";
            TypeNumberWidth = 2;
            LoadCategories();
            LoadSystems();
        }

        public ObservableCollection<string> ScopeModes { get; }
        public ObservableCollection<string> NumberingModes { get; }
        public ObservableCollection<NamingPreviewRow> Rows { get; }
        public ObservableCollection<NamingPreviewRow> FilteredRows { get; }
        public ObservableCollection<CategorySelectionItem> Categories { get; }
        public ObservableCollection<SystemRegistryEntry> Systems { get; }
        public ObservableCollection<string> Warnings { get; }
        public ICommand GeneratePreviewCommand { get; }
        public ICommand ApplyIfcNameCommand { get; }
        public ICommand ApplyIfcTypeCommand { get; }
        public ICommand ApplySystemDataCommand { get; }
        public ICommand ApplyAllCommand { get; }
        public ICommand ValidateCommand { get; }
        public ICommand ExportReportCommand { get; }
        public ICommand ApplyFiltersCommand { get; }
        public ICommand SelectAllRowsCommand { get; }
        public ICommand UnselectAllRowsCommand { get; }
        public ICommand FillDownPredefinedTypeCommand { get; }
        public ICommand RefreshCommand { get; }
        public NamingPreviewResult LastPreview { get; private set; }

        public string ScopeMode { get; set; }
        public string NumberingMode { get; set; }
        public bool UseFallbackCode { get; set; } = true;
        public string FallbackCode { get; set; }
        public string SelectedSystemName { get; set; }
        public bool AddAsNewSystem { get; set; } = true;
        public bool AppendToExistingSystem { get; set; }
        public int TypeNumberWidth { get; set; }
        public bool AllowDoorWindowUnassignedFallback { get; set; }
        private string _filterElementId;
        public string FilterElementId { get => _filterElementId; set { _filterElementId = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterCategory;
        public string FilterCategory { get => _filterCategory; set { _filterCategory = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterFamily;
        public string FilterFamily { get => _filterFamily; set { _filterFamily = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterType;
        public string FilterType { get => _filterType; set { _filterType = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterIfcEntity;
        public string FilterIfcEntity { get => _filterIfcEntity; set { _filterIfcEntity = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterIfcPredefinedType;
        public string FilterIfcPredefinedType { get => _filterIfcPredefinedType; set { _filterIfcPredefinedType = value; RaisePropertyChanged(); ApplyFilters(); } }
        public string FillDownIfcPredefinedType { get; set; }

        private string _status = "No preview generated.";
        public string Status { get => _status; set { _status = value; RaisePropertyChanged(); } }

        private void LoadCategories()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.GetCategories,
                Callback = r =>
                {
                    Categories.Clear();
                    foreach (var c in r.Categories ?? Enumerable.Empty<Category>()) Categories.Add(new CategorySelectionItem { Id = c.Id.Value, Name = c.Name, IsSelected = true });
                }
            });
        }

        private void LoadSystems()
        {
            _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.LoadSystemList, Callback = r =>
            {
                Systems.Clear();
                var loaded = r.Systems?.Count ?? 0;
                foreach (var s in r.Systems ?? Enumerable.Empty<SystemRegistryEntry>()) Systems.Add(s);
                var bound = Systems.Count;
                Status = $"Systems loaded: {loaded}, bound: {bound}, filtered out: {Math.Max(0, loaded - bound)}";
            }});
        }

        private void Generate()
        {
            var request = new NamingGenerationRequest
            {
                ScopeMode = Enum.TryParse(ScopeMode, out NamingScopeMode scope) ? scope : NamingScopeMode.CurrentSelection,
                InstanceNumberingMode = Enum.TryParse(NumberingMode, out InstanceNumberingMode mode) ? mode : InstanceNumberingMode.Sequential,
                CategoryIds = Categories.Where(c => c.IsSelected).Select(c => c.Id).ToList(),
                UseFallbackCode = UseFallbackCode,
                FallbackCode = FallbackCode,
                TypeNumberWidth = TypeNumberWidth <= 0 ? 2 : TypeNumberWidth,
                SelectedSystemName = SelectedSystemName,
                AllowDoorWindowUnassignedFallback = AllowDoorWindowUnassignedFallback,
                AddAsNewSystem = AddAsNewSystem,
                AppendToExistingSystem = AppendToExistingSystem
            };

            _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.GenerateNamingPreview, NamingRequest = request, Callback = r => ApplyPreview(r.NamingPreview) });
        }

        private void ValidateOnly()
        {
            if (LastPreview == null) { Status = "Generate preview first."; return; }
            Warnings.Clear();
            foreach (var w in LastPreview.Warnings) Warnings.Add(w);
            Status = $"Validation complete. Eligible: {LastPreview.EligibleCount}, Errors: {LastPreview.ErrorCount}, Warnings: {Warnings.Count}";
        }

        private void Apply(RevitRequestId requestId)
        {
            var selectedRows = requestId == RevitRequestId.ApplySystemData
                ? Rows.Where(r => r.IsSelected).ToList()
                : Rows.ToList();

            if (requestId == RevitRequestId.ApplySystemData && selectedRows.Count == 0)
            {
                Status = "No rows selected. Select at least one row before applying system data.";
                return;
            }

            _dispatcher.Raise(new RevitRequest
            {
                Id = requestId,
                NamingRows = selectedRows,
                Callback = r =>
                {
                    var updated = r.ApplyResult?.Updated ?? 0;
                    var skipped = r.ApplyResult?.Skipped ?? 0;
                    var logs = string.Join(" | ", r.ApplyResult?.Logs?.Take(3) ?? Enumerable.Empty<string>());
                    Status = $"Selected rows: {selectedRows.Count}, Updated: {updated}, Types: {r.ApplyResult?.UniqueTypesUpdated ?? 0}, Instances: {r.ApplyResult?.InstancesUpdated ?? 0}, ExportToIFCAs: {r.ApplyResult?.ExportAsUpdated ?? 0}, Skipped: {skipped}, Failed: {r.ApplyResult?.Failed ?? 0}{(string.IsNullOrWhiteSpace(logs) ? string.Empty : " | " + logs)}";
                    Generate();
                }
            });
        }

        private void Export()
        {
            var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "DfE_NamingReport.csv" };
            if (dialog.ShowDialog() != true) return;
            var lines = new[] { "Scope,Target,ElementId,Category,Family,Type,CurrentIFCName,ProposedIFCName,CurrentIFCNameType,ProposedIFCNameType,CurrentSystemName,ProposedSystemName,OriginalClassification,MatchedPrefix,ResolvedSystemTemplate,UserDefinedStatus,ValidationError,ProposedSystemCategory,ProposedIfcEntity,ProposedExportToIfcAs,ProposedIfcPredefinedType,Status" }
                .Concat(Rows.Select(r => $"Instance+Type,{r.ElementId},{r.ElementId},{r.Category},{r.Family},{r.Type},{r.CurrentIfcName},{r.ProposedIfcName},{r.CurrentIfcTypeName},{r.ProposedIfcTypeName},{r.CurrentSystemName},{r.ProposedSystemName},{r.SourceSsNumber},{r.MatchedSystemPrefix},{r.ProposedSystemDescription},{r.IsUserDefinedSystem},{r.UserDefinedValidationError},{r.ProposedSystemCategory},{r.ProposedIfcEntity},{r.ProposedIfcExportAs},{r.ProposedIfcPredefinedType},{r.Status}"));
            File.WriteAllLines(dialog.FileName, lines);
        }

        private void ApplyPreview(NamingPreviewResult preview)
        {
            LastPreview = preview;
            Rows.Clear();
            foreach (var row in preview?.Rows ?? Enumerable.Empty<NamingPreviewRow>()) Rows.Add(row);
            ApplyFilters();
            Warnings.Clear();
            foreach (var warning in preview?.Warnings ?? Enumerable.Empty<string>()) Warnings.Add(warning);
            Status = $"Selected: {preview?.SelectedCount ?? 0}, Eligible: {preview?.EligibleCount ?? 0}, Skipped: {preview?.SkippedCount ?? 0}, Errors: {preview?.ErrorCount ?? 0}";
        }

        public void ApplyFilters()
        {
            FilteredRows.Clear();
            foreach (var row in Rows.Where(MatchesNamingFilter))
            {
                FilteredRows.Add(row);
            }
            Debug.WriteLine($"[DfEIfcNamer] Naming filter rebuild: total={Rows.Count}, visible={FilteredRows.Count}");
        }

        private bool MatchesNamingFilter(NamingPreviewRow row)
        {
            return Contains(row.ElementId.ToString(), FilterElementId)
                   && Contains(row.Category, FilterCategory)
                   && Contains(row.Family, FilterFamily)
                   && Contains(row.Type, FilterType)
                   && Contains(row.ProposedIfcEntity, FilterIfcEntity)
                   && Contains(row.ProposedIfcPredefinedType, FilterIfcPredefinedType);
        }

        private void SetSelection(bool selected)
        {
            if (FilteredRows.Count == 0) ApplyFilters();
            foreach (var row in FilteredRows)
            {
                row.IsSelected = selected;
            }
            Status = $"Selection updated: {FilteredRows.Count(x => x.IsSelected)}/{FilteredRows.Count} visible rows selected.";
            Debug.WriteLine($"[DfEIfcNamer] Naming selection change: selected={FilteredRows.Count(x => x.IsSelected)}, visible={FilteredRows.Count}");
        }

        private void FillDownPredefinedType()
        {
            var value = FillDownIfcPredefinedType;
            if (string.IsNullOrWhiteSpace(value))
            {
                var seed = FilteredRows.FirstOrDefault(x => x.IsSelected);
                value = seed?.ProposedIfcPredefinedType;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                Status = "Fill-down skipped: no IFC predefined type selected.";
                return;
            }

            foreach (var row in FilteredRows.Where(x => x.IsSelected))
            {
                row.ProposedIfcPredefinedType = value;
            }
            Status = $"Filled IFC Predefined Type '{value}' to {FilteredRows.Count(x => x.IsSelected)} selected rows.";
        }

        private static bool Contains(string source, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return (source ?? string.Empty).IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public class HeaderDataViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;
        private readonly TemplateConfigService _template = new TemplateConfigService();

        public HeaderDataViewModel(RevitRequestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            ReadFromModelCommand = new RelayCommand(_ => Read());
            ValidateCommand = new RelayCommand(_ => Validate());
            WriteToModelCommand = new RelayCommand(_ => Write());
            SaveTemplateCommand = new RelayCommand(_ => SaveTemplate());
            LoadTemplateCommand = new RelayCommand(_ => LoadTemplate());
            ExportSummaryCommand = new RelayCommand(_ => Export());
            ValidationMessages = new ObservableCollection<string>();
        }

        public HeaderDataModel Data { get; private set; } = new HeaderDataModel();
        public HeaderValidationResult LastValidation { get; private set; } = new HeaderValidationResult();
        public ObservableCollection<string> ValidationMessages { get; }
        public ICommand ReadFromModelCommand { get; }
        public ICommand ValidateCommand { get; }
        public ICommand WriteToModelCommand { get; }
        public ICommand SaveTemplateCommand { get; }
        public ICommand LoadTemplateCommand { get; }
        public ICommand ExportSummaryCommand { get; }

        public string IfcProjectName { get => Data.IfcProjectName; set { Data.IfcProjectName = value; RaisePropertyChanged(); } }
        public string IfcProjectDescription { get => Data.IfcProjectDescription; set { Data.IfcProjectDescription = value; RaisePropertyChanged(); } }
        public string IfcSiteName { get => Data.IfcSiteName; set { Data.IfcSiteName = value; RaisePropertyChanged(); } }
        public string IfcSiteDescription { get => Data.IfcSiteDescription; set { Data.IfcSiteDescription = value; RaisePropertyChanged(); } }
        public string IfcBuildingName { get => Data.IfcBuildingName; set { Data.IfcBuildingName = value; RaisePropertyChanged(); } }
        public string IfcBuildingDescription { get => Data.IfcBuildingDescription; set { Data.IfcBuildingDescription = value; RaisePropertyChanged(); } }
        public string UPRN { get => Data.UPRN; set { Data.UPRN = value; RaisePropertyChanged(); } }
        public string MaximumBlockHeight { get => Data.MaximumBlockHeight; set { Data.MaximumBlockHeight = value; RaisePropertyChanged(); } }
        public string NumberOfStoreys { get => Data.NumberOfStoreys; set { Data.NumberOfStoreys = value; RaisePropertyChanged(); } }
        public string Phase { get => Data.Phase; set { Data.Phase = value; RaisePropertyChanged(); } }
        public string BlockConstructionType { get => Data.BlockConstructionType; set { Data.BlockConstructionType = value; RaisePropertyChanged(); } }

        private string _status = "No header data loaded.";
        public string Status { get => _status; set { _status = value; RaisePropertyChanged(); } }

        private void Read()
        {
            _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.ReadHeaderData, Callback = r => { Data = r.HeaderData ?? new HeaderDataModel(); RefreshAll(); Status = "Header data read from model."; } });
        }

        private void Validate()
        {
            _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.ValidateHeaderData, HeaderData = Data, Callback = r => ApplyValidation(r.HeaderValidation) });
        }

        private void Write()
        {
            _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.WriteHeaderData, HeaderData = Data, Callback = r => { ApplyValidation(r.HeaderValidation); Status = $"Write complete. Updated {r.ApplyResult?.Updated ?? 0}, skipped {r.ApplyResult?.Skipped ?? 0}."; } });
        }

        private void SaveTemplate()
        {
            var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = "DfE_HeaderTemplate.json" };
            if (d.ShowDialog() != true) return;
            _template.SaveHeaderTemplate(d.FileName, Data);
            Status = "Template saved.";
        }

        private void LoadTemplate()
        {
            var d = new OpenFileDialog { Filter = "JSON|*.json" };
            if (d.ShowDialog() != true) return;
            Data = _template.LoadHeaderTemplate(d.FileName);
            RefreshAll();
            Status = "Template loaded.";
        }

        private void Export()
        {
            var d = new SaveFileDialog { Filter = "Text|*.txt", FileName = "DfE_HeaderSummary.txt" };
            if (d.ShowDialog() != true) return;
            File.WriteAllText(d.FileName, $"Project: {IfcProjectName}\nSite: {IfcSiteName}\nBuilding: {IfcBuildingName}\nUPRN: {UPRN}\nMaximumBlockHeight: {MaximumBlockHeight}\nNumberOfStoreys: {NumberOfStoreys}\nPhase: {Phase}\nBlockConstructionType: {BlockConstructionType}");
            Status = "Header summary exported.";
        }

        private void ApplyValidation(HeaderValidationResult validation)
        {
            LastValidation = validation ?? new HeaderValidationResult();
            ValidationMessages.Clear();
            foreach (var m in LastValidation.Messages) ValidationMessages.Add(m);
            if (!ValidationMessages.Any()) ValidationMessages.Add("No validation warnings.");
        }

        private void RefreshAll()
        {
            RaisePropertyChanged(nameof(IfcProjectName)); RaisePropertyChanged(nameof(IfcProjectDescription)); RaisePropertyChanged(nameof(IfcSiteName));
            RaisePropertyChanged(nameof(IfcSiteDescription)); RaisePropertyChanged(nameof(IfcBuildingName)); RaisePropertyChanged(nameof(IfcBuildingDescription));
            RaisePropertyChanged(nameof(UPRN)); RaisePropertyChanged(nameof(MaximumBlockHeight));
            RaisePropertyChanged(nameof(NumberOfStoreys)); RaisePropertyChanged(nameof(Phase)); RaisePropertyChanged(nameof(BlockConstructionType));
        }
    }

    public class SystemAssignmentViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;
        private readonly NamingViewModel _naming;
        private readonly SystemCatalogService _catalogService = new SystemCatalogService();
        private readonly ObservableCollection<string> _existingSystemNames = new ObservableCollection<string>();

        public SystemAssignmentViewModel(RevitRequestDispatcher dispatcher, NamingViewModel naming)
        {
            _dispatcher = dispatcher;
            _naming = naming;
            SuggestedSystems = new ObservableCollection<SystemCandidateOption>();
            AllAllowedSystems = new ObservableCollection<string>();
            FilteredRows = new ObservableCollection<NamingPreviewRow>();
            AutoResolveCommand = new RelayCommand(_ => AutoResolve());
            ValidateCommand = new RelayCommand(_ => Validate());
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            UnselectAllCommand = new RelayCommand(_ => SetSelection(false));
            ApplyFiltersCommand = new RelayCommand(_ => ApplyFilters());
            ApplyCommand = _naming.ApplySystemDataCommand;
            ExportCommand = _naming.ExportReportCommand;
            LoadAllAllowedSystems();
            LoadExistingSystemNames();
            _naming.Rows.CollectionChanged += (s, e) => ApplyFilters();
            ApplyFilters();
        }

        public ObservableCollection<NamingPreviewRow> Rows => _naming.Rows;
        public ObservableCollection<NamingPreviewRow> FilteredRows { get; }
        public ObservableCollection<string> Warnings => _naming.Warnings;
        public ObservableCollection<SystemCandidateOption> SuggestedSystems { get; }
        public ObservableCollection<string> AllAllowedSystems { get; }
        public ICommand AutoResolveCommand { get; }
        public ICommand ValidateCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand UnselectAllCommand { get; }
        public ICommand ApplyFiltersCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand ExportCommand { get; }

        public string UserDefinedName { get; set; }
        public string UserDefinedDescription { get; set; }
        public bool AddAsNextSystem { get; set; }
        public string SelectedSuggestedSystem { get; set; }
        public string SelectedAllowedSystem { get; set; }
        private string _rowFilterText;
        public string RowFilterText { get => _rowFilterText; set { _rowFilterText = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterCategory;
        public string FilterCategory { get => _filterCategory; set { _filterCategory = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterFamily;
        public string FilterFamily { get => _filterFamily; set { _filterFamily = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterType;
        public string FilterType { get => _filterType; set { _filterType = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterSsNumber;
        public string FilterSsNumber { get => _filterSsNumber; set { _filterSsNumber = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterResolvedSystem;
        public string FilterResolvedSystem { get => _filterResolvedSystem; set { _filterResolvedSystem = value; RaisePropertyChanged(); ApplyFilters(); } }

        private string _status = "System assignment ready.";
        public string Status { get => _status; set { _status = value; RaisePropertyChanged(); } }

        private void AutoResolve()
        {
            var selected = Rows.Where(r => r.IsSelected).ToList();
            var relevantRows = selected.Any() ? selected : Rows.ToList();
            var withSs = relevantRows.Where(r => !string.IsNullOrWhiteSpace(r.SourceSsNumber)).ToList();

            var candidates = withSs
                .SelectMany(r => _catalogService.ResolveCandidatesByClassification(r.SourceSsNumber)
                    .Select(c => new SystemCandidateOption { SystemName = c.SystemName, MatchedPrefix = c.MatchedPrefix, MatchLength = c.MatchedPrefix?.Length ?? 0 }))
                .GroupBy(c => c.SystemName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.MatchLength).First())
                .OrderByDescending(x => x.MatchLength)
                .ThenBy(x => x.SystemName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SuggestedSystems.Clear();
            foreach (var candidate in candidates) SuggestedSystems.Add(candidate);
            if (SuggestedSystems.Any()) SelectedSuggestedSystem = SuggestedSystems.First().SystemName;

            var chosen = !string.IsNullOrWhiteSpace(SelectedSuggestedSystem) ? SelectedSuggestedSystem : SelectedAllowedSystem;
            foreach (var row in relevantRows)
            {
                var rowBest = _catalogService.ResolveCandidatesByClassification(row.SourceSsNumber).FirstOrDefault()?.SystemName;
                var preferred = !string.IsNullOrWhiteSpace(chosen) ? chosen : rowBest;
                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    row.ProposedSystemName = AddAsNextSystem ? NextSystemName(preferred) : preferred;
                    row.IsUserDefinedSystem = false;
                }
                else if (string.IsNullOrWhiteSpace(row.CandidateSystems))
                {
                    row.IsUserDefinedSystem = true;
                }
            }

            var unresolved = relevantRows.Count(r => string.IsNullOrWhiteSpace(r.CandidateSystems));
            Status = $"Rows selected: {relevantRows.Count}, rows with Ss number: {withSs.Count}, candidate systems found: {SuggestedSystems.Count}, unmatched rows: {unresolved}.";
            ApplyFilters();
        }

        private void Validate()
        {
            var invalid = 0;
            foreach (var row in Rows.Where(r => r.IsSelected && r.IsUserDefinedSystem))
            {
                row.ProposedSystemName = UserDefinedName;
                row.ProposedSystemDescription = UserDefinedDescription;
                row.UserDefinedValidationError = AuthoringNamingService.ValidateUserDefinedSystemName(UserDefinedName);
                if (!string.IsNullOrWhiteSpace(row.UserDefinedValidationError))
                {
                    invalid++;
                }
            }

            Status = invalid > 0
                ? $"Validation failed for {invalid} selected USERDEFINED rows. Expected ^[A-Z][A-Za-z0-9]*_System\\d{{2}}$."
                : "User-defined system input valid.";
        }

        private void LoadAllAllowedSystems()
        {
            AllAllowedSystems.Clear();
            foreach (var item in _catalogService.GetAllAllowedSystems().Select(x => x.SystemName).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AllAllowedSystems.Add(item);
            }
        }

        private void LoadExistingSystemNames()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.GetExistingSystemNames,
                Callback = r =>
                {
                    _existingSystemNames.Clear();
                    foreach (var value in r.ExistingSystemNames ?? Enumerable.Empty<string>()) _existingSystemNames.Add(value);
                }
            });
        }

        private string NextSystemName(string selectedBase)
        {
            var baseName = (selectedBase ?? string.Empty).Replace("_SystemXX", string.Empty).Trim();
            var pattern = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(baseName) + "_System(\\d{2})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var max = 0;
            foreach (var existing in _existingSystemNames)
            {
                var match = pattern.Match(existing ?? string.Empty);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
                {
                    max = Math.Max(max, parsed);
                }
            }

            return $"{baseName}_System{(max + 1):00}";
        }

        private void SelectAll()
        {
            SetSelection(true);
        }

        private void SetSelection(bool value)
        {
            if (FilteredRows.Count == 0) ApplyFilters();
            foreach (var row in FilteredRows)
            {
                row.IsSelected = value;
            }
        }

        public void ApplyFilters()
        {
            FilteredRows.Clear();
            foreach (var row in Rows.Where(MatchesFilter))
            {
                FilteredRows.Add(row);
            }
        }

        private bool MatchesFilter(NamingPreviewRow row)
        {
            return Contains(row.Category, FilterCategory)
                   && Contains(row.Family, FilterFamily)
                   && Contains(row.Type, FilterType)
                   && Contains(row.SourceSsNumber, FilterSsNumber)
                   && Contains(row.ProposedSystemName, FilterResolvedSystem)
                   && (string.IsNullOrWhiteSpace(RowFilterText)
                       || Contains(row.Category, RowFilterText)
                       || Contains(row.SourceSsNumber, RowFilterText)
                       || Contains(row.ProposedSystemName, RowFilterText));
        }

        private static bool Contains(string source, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return (source ?? string.Empty).IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public class SpaceZoneViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;
        public SpaceZoneViewModel(RevitRequestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            Rows = new ObservableCollection<SpaceZonePreviewRow>();
            FilteredRows = new ObservableCollection<SpaceZonePreviewRow>();
            Zones = new ObservableCollection<ZoneCatalogEntry>();
            AdsClassifications = new ObservableCollection<AdsClassificationEntry>();
            LoadSelectionCommand = new RelayCommand(_ => Resolve());
            ResolveRoomsCommand = new RelayCommand(_ => Resolve());
            SelectAllRowsCommand = new RelayCommand(_ => SetAllSelections(true));
            UnselectAllRowsCommand = new RelayCommand(_ => SetAllSelections(false));
            ApplySpaceReferenceCommand = new RelayCommand(_ => ApplySpace());
            ApplyZoneNameCommand = new RelayCommand(_ => ApplyZone());
            ApplyAdsCommand = new RelayCommand(_ => ApplyAds());
            ValidateAssignmentCommand = new RelayCommand(_ => Validate());
            ExportReportCommand = new RelayCommand(_ => Export());
            ApplyFiltersCommand = new RelayCommand(_ => ApplyFilters());
            RefreshCommand = new RelayCommand(_ => Resolve());
        }

        public ObservableCollection<SpaceZonePreviewRow> Rows { get; }
        public ObservableCollection<SpaceZonePreviewRow> FilteredRows { get; }
        public ObservableCollection<ZoneCatalogEntry> Zones { get; }
        public ObservableCollection<AdsClassificationEntry> AdsClassifications { get; }
        public SpaceZonePreviewResult LastPreview { get; private set; }
        public ICommand LoadSelectionCommand { get; }
        public ICommand ResolveRoomsCommand { get; }
        public ICommand SelectAllRowsCommand { get; }
        public ICommand UnselectAllRowsCommand { get; }
        public ICommand ApplySpaceReferenceCommand { get; }
        public ICommand ApplyZoneNameCommand { get; }
        public ICommand ApplyAdsCommand { get; }
        public ICommand ValidateAssignmentCommand { get; }
        public ICommand ExportReportCommand { get; }
        public ICommand ApplyFiltersCommand { get; }
        public ICommand RefreshCommand { get; }
        public string ProposedZoneName { get; set; }
        public string ProposedAdsClassification { get; set; }
        private string _filterCategory;
        public string FilterCategory { get => _filterCategory; set { _filterCategory = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterFamily;
        public string FilterFamily { get => _filterFamily; set { _filterFamily = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterType;
        public string FilterType { get => _filterType; set { _filterType = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterRoom;
        public string FilterRoom { get => _filterRoom; set { _filterRoom = value; RaisePropertyChanged(); ApplyFilters(); } }
        private string _filterZone;
        public string FilterZone { get => _filterZone; set { _filterZone = value; RaisePropertyChanged(); ApplyFilters(); } }

        private string _status = "No selection loaded.";
        public string Status { get => _status; set { _status = value; RaisePropertyChanged(); } }

        private void Resolve()
        {
            _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.ResolveSpaceZone, SpaceZoneRequest = new SpaceZoneRequest { ProposedZoneName = ProposedZoneName, ProposedAdsClassification = ProposedAdsClassification }, Callback = r =>
            {
                LastPreview = r.SpaceZonePreview;
                Rows.Clear();
                foreach (var row in r.SpaceZonePreview?.Rows ?? Enumerable.Empty<SpaceZonePreviewRow>()) Rows.Add(row);
                ApplyFilters();
                Zones.Clear();
                foreach (var zone in r.Zones ?? Enumerable.Empty<ZoneCatalogEntry>()) Zones.Add(zone);
                AdsClassifications.Clear();
                foreach (var ads in r.AdsClassifications ?? Enumerable.Empty<AdsClassificationEntry>()) AdsClassifications.Add(ads);
                Status = $"Loaded {Rows.Count} valid Room/Space rows, selected: {Rows.Count(x => x.IsSelected)}, skipped non-room/space: {r.SpaceZonePreview?.SkippedNonRoomSpaceCount ?? 0}, missing refs: {r.SpaceZonePreview?.MissingRoomCount ?? 0}, zones loaded: {Zones.Count}, ADS loaded: {AdsClassifications.Count}";
            }});
        }

        private void SetAllSelections(bool selected)
        {
            if (FilteredRows.Count == 0) ApplyFilters();
            foreach (var row in FilteredRows)
            {
                row.IsSelected = selected;
            }

            var selectedCount = FilteredRows.Count(x => x.IsSelected);
            Status = $"Visible rows selected: {selectedCount}/{FilteredRows.Count}.";
        }

        public void ApplyFilters()
        {
            FilteredRows.Clear();
            foreach (var row in Rows.Where(MatchesFilter))
            {
                FilteredRows.Add(row);
            }
        }

        private bool MatchesFilter(SpaceZonePreviewRow row)
        {
            return Contains(row.Category, FilterCategory)
                   && Contains(row.Family, FilterFamily)
                   && Contains(row.Type, FilterType)
                   && Contains(row.RoomNumber, FilterRoom)
                   && Contains(row.ProposedZoneName, FilterZone);
        }

        private static bool Contains(string source, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return (source ?? string.Empty).IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplySpace()
        {
            var selectedRows = Rows.Where(r => r.IsSelected).ToList();
            if (selectedRows.Count == 0)
            {
                Status = "No rows selected. Select at least one row before applying SpaceReference.";
                return;
            }

            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.ApplySpaceReference,
                SpaceZoneRows = selectedRows,
                Callback = r =>
                {
                    var updated = r.ApplyResult?.Updated ?? 0;
                    var skipped = r.ApplyResult?.Skipped ?? 0;
                    Status = $"SpaceReference apply - selected: {selectedRows.Count}, updated: {updated}, skipped: {skipped}, failed: {r.ApplyResult?.Failed ?? 0}";
                    Resolve();
                }
            });
        }

        private void ApplyZone()
        {
            var selectedRows = Rows.Where(r => r.IsSelected).ToList();
            if (selectedRows.Count == 0)
            {
                Status = "No rows selected. Select at least one row before applying ZoneName.";
                return;
            }

            foreach (var row in selectedRows) row.ProposedZoneName = ProposedZoneName;
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.ApplyZoneName,
                SpaceZoneRows = selectedRows,
                Callback = r =>
                {
                    var updated = r.ApplyResult?.Updated ?? 0;
                    var skipped = r.ApplyResult?.Skipped ?? 0;
                    Status = $"Zone apply - selected: {selectedRows.Count}, updated: {updated}, skipped: {skipped}, failed: {r.ApplyResult?.Failed ?? 0}";
                    Resolve();
                }
            });
        }

        private void ApplyAds()
        {
            var selectedRows = Rows.Where(r => r.IsSelected).ToList();
            if (selectedRows.Count == 0)
            {
                Status = "No rows selected. Select at least one row before applying ADS.";
                return;
            }

            foreach (var row in selectedRows)
            {
                row.ProposedAdsClassification = ProposedAdsClassification;
                row.ProposedAdsText = ProposedAdsClassification?.Split(new[] { " : " }, StringSplitOptions.None).FirstOrDefault() ?? ProposedAdsClassification;
            }

            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.ApplyAdsClassification,
                SpaceZoneRows = selectedRows,
                Callback = r =>
                {
                    var updated = r.ApplyResult?.Updated ?? 0;
                    var skipped = r.ApplyResult?.Skipped ?? 0;
                    Status = $"ADS apply - selected: {selectedRows.Count}, updated: {updated}, skipped: {skipped}, failed: {r.ApplyResult?.Failed ?? 0}, ADS classification updated: {r.ApplyResult?.AdsClassificationUpdated ?? 0}, ADS text updated: {r.ApplyResult?.AdsTextUpdated ?? 0}";
                    Resolve();
                }
            });
        }

        private void Validate()
        {
            var missing = Rows.Count(r => string.IsNullOrWhiteSpace(r.RoomNumber));
            var missingZone = Rows.Count(r => string.IsNullOrWhiteSpace(r.ProposedZoneName));
            var missingSpaceRef = Rows.Count(r => string.IsNullOrWhiteSpace(r.ProposedSpaceReference));
            var missingAdsCode = Rows.Count(r => string.IsNullOrWhiteSpace(r.ProposedAdsText));
            var missingAdsDescription = Rows.Count(r => !string.IsNullOrWhiteSpace(r.ProposedAdsText) && (string.IsNullOrWhiteSpace(r.ProposedAdsClassification) || !r.ProposedAdsClassification.Contains(" - ")));
            var mismatchedAds = Rows.Count(r => !string.IsNullOrWhiteSpace(r.ProposedAdsClassification) && !string.IsNullOrWhiteSpace(r.ProposedAdsText) && !r.ProposedAdsClassification.Contains(r.ProposedAdsText));
            Status = $"Validation: rows={Rows.Count}, missing room refs={missing}, missing zone={missingZone}, missing space ref={missingSpaceRef}, missing ADS code={missingAdsCode}, missing ADS description={missingAdsDescription}, ADS mismatches={mismatchedAds}";
        }

        private void Export()
        {
            var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "DfE_SpaceZoneReport.csv" };
            if (d.ShowDialog() != true) return;
            var lines = new[] { "Scope,Target,ElementId,Category,FamilyType,Level,RoomNumber,RoomName,CurrentSpaceReference,ProposedSpaceReference,CurrentZoneName,ProposedZoneName,CurrentAdsText,ProposedAdsText,CurrentAdsClassification,ProposedAdsClassification,Status" }
                .Concat(Rows.Select(r => $"Room,{r.ElementId},{r.ElementId},{r.Category},{r.FamilyType},{r.Level},{r.RoomNumber},{r.RoomName},{r.CurrentSpaceReference},{r.ProposedSpaceReference},{r.CurrentZoneName},{r.ProposedZoneName},{r.CurrentAdsText},{r.ProposedAdsText},{r.CurrentAdsClassification},{r.ProposedAdsClassification},{r.Status}"));
            File.WriteAllLines(d.FileName, lines);
        }
    }

    public class ClassificationSyncViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;
        public ClassificationSyncViewModel(RevitRequestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            Rows = new ObservableCollection<ClassificationSyncPreviewRow>();
            Warnings = new ObservableCollection<string>();
            GeneratePreviewCommand = new RelayCommand(_ => Generate());
            ApplyCommand = new RelayCommand(_ => Apply());
        }

        public ObservableCollection<ClassificationSyncPreviewRow> Rows { get; }
        public ObservableCollection<string> Warnings { get; }
        public ICommand GeneratePreviewCommand { get; }
        public ICommand ApplyCommand { get; }
        private bool _canApply = true;
        public bool CanApply { get => _canApply; set { _canApply = value; RaisePropertyChanged(); } }
        private string _status = "Not run.";
        public string Status { get => _status; set { _status = value; RaisePropertyChanged(); } }

        private void Generate()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.GenerateClassificationSyncPreview,
                Callback = r =>
                {
                    Rows.Clear();
                    foreach (var row in r.ClassificationSyncResult?.Rows ?? Enumerable.Empty<ClassificationSyncPreviewRow>()) Rows.Add(row);
                    Warnings.Clear();
                    foreach (var w in r.ClassificationSyncResult?.Warnings ?? Enumerable.Empty<string>()) Warnings.Add(w);
                    var selected = r.ClassificationSyncResult?.SelectedCount ?? 0;
                    var classified = r.ClassificationSyncResult?.ClassifiedCount ?? 0;
                    var missing = r.ClassificationSyncResult?.MissingClassificationCount ?? 0;
                    CanApply = classified > 0;
                    Status = $"Selected: {selected}, classified: {classified}, missing classification: {missing}. Preview rows: {Rows.Count}, type targets: {r.ClassificationSyncResult?.TypeTargets ?? 0}, instance targets: {r.ClassificationSyncResult?.InstanceTargets ?? 0}";
                }
            });
        }

        private void Apply()
        {
            if (!CanApply)
            {
                Status = "Apply disabled: no valid Classification.Uniclass values available to sync.";
                return;
            }
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.ApplyClassificationSync,
                ClassificationSyncRows = Rows.ToList(),
                Callback = r => Status = $"Classification sync applied. Updated: {r.ApplyResult?.Updated ?? 0}, unique types: {r.ApplyResult?.UniqueTypesUpdated ?? 0}, instances: {r.ApplyResult?.InstancesUpdated ?? 0}"
            });
        }
    }

    public class ComplianceValidationViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;
        private readonly ICollectionView _rowsView;

        public ComplianceValidationViewModel(RevitRequestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            Rows = new ObservableCollection<ComplianceCheckResult>();
            Categories = new ObservableCollection<string> { "All" };
            Families = new ObservableCollection<string> { "All" };
            Types = new ObservableCollection<string> { "All" };
            RuleGroups = new ObservableCollection<string> { "All" };
            StatusModes = new ObservableCollection<string> { "All", "Non-compliant only", "Compliant only" };
            SelectedStatusMode = StatusModes[0];
            _rowsView = CollectionViewSource.GetDefaultView(Rows);
            _rowsView.Filter = FilterRow;

            RunValidationCommand = new RelayCommand(_ => RunValidation());
            RefreshCommand = new RelayCommand(_ => RunValidation());
            ViewIn3DCommand = new RelayCommand(_ => ViewIn3DModel());
            SelectAllCommand = new RelayCommand(_ => SetSelection(true));
            UnselectAllCommand = new RelayCommand(_ => SetSelection(false));
        }

        public ObservableCollection<ComplianceCheckResult> Rows { get; }
        public ICollectionView FilteredRows => _rowsView;
        public ObservableCollection<string> Categories { get; }
        public ObservableCollection<string> Families { get; }
        public ObservableCollection<string> Types { get; }
        public ObservableCollection<string> RuleGroups { get; }
        public ObservableCollection<string> StatusModes { get; }
        public ICommand RunValidationCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ViewIn3DCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand UnselectAllCommand { get; }

        private string _selectedCategory = "All";
        public string SelectedCategory { get => _selectedCategory; set { _selectedCategory = value; RaisePropertyChanged(); RefreshFilter(); } }
        private string _selectedFamily = "All";
        public string SelectedFamily { get => _selectedFamily; set { _selectedFamily = value; RaisePropertyChanged(); RefreshFilter(); } }
        private string _selectedType = "All";
        public string SelectedType { get => _selectedType; set { _selectedType = value; RaisePropertyChanged(); RefreshFilter(); } }
        private string _selectedRuleGroup = "All";
        public string SelectedRuleGroup { get => _selectedRuleGroup; set { _selectedRuleGroup = value; RaisePropertyChanged(); RefreshFilter(); } }
        private string _selectedStatusMode;
        public string SelectedStatusMode { get => _selectedStatusMode; set { _selectedStatusMode = value; RaisePropertyChanged(); RefreshFilter(); } }

        private int _totalElementsChecked;
        public int TotalElementsChecked { get => _totalElementsChecked; set { _totalElementsChecked = value; RaisePropertyChanged(); } }
        private int _compliantElementsCount;
        public int CompliantElementsCount { get => _compliantElementsCount; set { _compliantElementsCount = value; RaisePropertyChanged(); } }
        private int _nonCompliantElementsCount;
        public int NonCompliantElementsCount { get => _nonCompliantElementsCount; set { _nonCompliantElementsCount = value; RaisePropertyChanged(); } }
        private int _totalFailedChecks;
        public int TotalFailedChecks { get => _totalFailedChecks; set { _totalFailedChecks = value; RaisePropertyChanged(); } }
        private string _elementCompliancePercent = "0%";
        public string ElementCompliancePercent { get => _elementCompliancePercent; set { _elementCompliancePercent = value; RaisePropertyChanged(); } }
        private string _ruleCompliancePercent = "0%";
        public string RuleCompliancePercent { get => _ruleCompliancePercent; set { _ruleCompliancePercent = value; RaisePropertyChanged(); } }
        private string _metricDefinition = string.Empty;
        public string MetricDefinition { get => _metricDefinition; set { _metricDefinition = value; RaisePropertyChanged(); } }
        private string _status = "Not run.";
        public string Status { get => _status; set { _status = value; RaisePropertyChanged(); } }

        private bool FilterRow(object obj)
        {
            var row = obj as ComplianceCheckResult;
            if (row == null) return false;
            if (!Matches(SelectedCategory, row.Category)) return false;
            if (!Matches(SelectedFamily, row.Family)) return false;
            if (!Matches(SelectedType, row.Type)) return false;
            if (!Matches(SelectedRuleGroup, row.RuleGroup)) return false;
            if (SelectedStatusMode == "Non-compliant only" && !row.IsFailed) return false;
            if (SelectedStatusMode == "Compliant only" && !row.IsCompliant) return false;
            return true;
        }

        private static bool Matches(string selected, string current)
        {
            return string.IsNullOrWhiteSpace(selected) || selected == "All" || string.Equals(selected, current ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshFilter() => _rowsView?.Refresh();

        private void SetSelection(bool value)
        {
            foreach (var row in _rowsView.Cast<ComplianceCheckResult>())
            {
                row.IsSelected = value;
            }
            _rowsView.Refresh();
        }

        private void RunValidation()
        {
            Status = "Running DfE compliance validation...";
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.RunComplianceValidation,
                Callback = r =>
                {
                    var summary = r.ComplianceSummary ?? new ComplianceRunSummary();
                    Rows.Clear();
                    foreach (var row in summary.Rows) Rows.Add(row);
                    RebuildFilters();
                    TotalElementsChecked = summary.TotalElementsChecked;
                    CompliantElementsCount = summary.CompliantElementsCount;
                    NonCompliantElementsCount = summary.NonCompliantElementsCount;
                    TotalFailedChecks = summary.FailedApplicableChecks;
                    ElementCompliancePercent = summary.ElementCompliancePercent.ToString("0.0") + "%";
                    RuleCompliancePercent = summary.RuleCompliancePercent.ToString("0.0") + "%";
                    MetricDefinition = summary.MetricDefinition;
                    Status = $"Validation complete. Checks: {summary.TotalApplicableChecks}, failed: {summary.FailedApplicableChecks}.";
                }
            });
        }

        private void ViewIn3DModel()
        {
            var selectedFailedElementIds = _rowsView.Cast<ComplianceCheckResult>()
                .Where(x => x.IsSelected && x.IsFailed && x.ElementId > 0)
                .Select(x => x.ElementId)
                .Distinct()
                .ToList();
            if (!selectedFailedElementIds.Any())
            {
                Status = "No selected non-compliant elements in current filter.";
                return;
            }

            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.OpenComplianceReview3d,
                ElementIds = selectedFailedElementIds,
                Callback = r =>
                {
                    Status = r.ApplyResult?.Logs?.FirstOrDefault() ?? $"Opened 3D review for {selectedFailedElementIds.Count} element(s).";
                }
            });
        }

        private void RebuildFilters()
        {
            ResetFilterCollection(Categories, Rows.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
            ResetFilterCollection(Families, Rows.Select(x => x.Family).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
            ResetFilterCollection(Types, Rows.Select(x => x.Type).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
            ResetFilterCollection(RuleGroups, Rows.Select(x => x.RuleGroup).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
            SelectedCategory = "All";
            SelectedFamily = "All";
            SelectedType = "All";
            SelectedRuleGroup = "All";
            RefreshFilter();
        }

        private static void ResetFilterCollection(ObservableCollection<string> collection, IEnumerable<string> values)
        {
            collection.Clear();
            collection.Add("All");
            foreach (var value in values) collection.Add(value);
        }
    }

    public class ValidationViewModel : ViewModelBase
    {
        private readonly RevitRequestDispatcher _dispatcher;
        private readonly SetupViewModel _setup;
        private readonly NamingViewModel _naming;
        private readonly HeaderDataViewModel _header;
        private readonly SpaceZoneViewModel _space;

        public ValidationViewModel(RevitRequestDispatcher dispatcher, SetupViewModel setup, NamingViewModel naming, HeaderDataViewModel header, SpaceZoneViewModel space)
        {
            _dispatcher = dispatcher;
            _setup = setup;
            _naming = naming;
            _header = header;
            _space = space;
            Messages = new ObservableCollection<string>();
            RunValidationCommand = new RelayCommand(_ => RunValidation());
            SyncCobieCommand = new RelayCommand(_ => SyncCobie());
            ExportReportCommand = new RelayCommand(_ => Export());
        }

        public ObservableCollection<string> Messages { get; }
        public ICommand RunValidationCommand { get; }
        public ICommand SyncCobieCommand { get; }
        public ICommand ExportReportCommand { get; }

        private string _setupReadiness = "Unknown";
        public string SetupReadiness { get => _setupReadiness; set { _setupReadiness = value; RaisePropertyChanged(); } }
        private string _namingCompleteness = "Unknown";
        public string NamingCompleteness { get => _namingCompleteness; set { _namingCompleteness = value; RaisePropertyChanged(); } }
        private string _headerCompleteness = "Unknown";
        public string HeaderCompleteness { get => _headerCompleteness; set { _headerCompleteness = value; RaisePropertyChanged(); } }
        private string _spaceZoneCompleteness = "Unknown";
        public string SpaceZoneCompleteness { get => _spaceZoneCompleteness; set { _spaceZoneCompleteness = value; RaisePropertyChanged(); } }

        private string _status = "Validation not run.";
        public string Status { get => _status; set { _status = value; RaisePropertyChanged(); } }

        private void RunValidation()
        {
            _dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.RunAuthoringValidation,
                SetupSnapshot = _setup.LastResult,
                NamingSnapshot = _naming.LastPreview,
                HeaderSnapshot = _header.LastValidation,
                SpaceZoneSnapshot = _space.LastPreview,
                Callback = r =>
                {
                    var summary = r.ValidationSummary;
                    SetupReadiness = summary?.SetupReadiness ?? "Unknown";
                    NamingCompleteness = summary?.NamingCompleteness ?? "Unknown";
                    HeaderCompleteness = summary?.HeaderCompleteness ?? "Unknown";
                    SpaceZoneCompleteness = summary?.SpaceZoneCompleteness ?? "Unknown";
                    Messages.Clear();
                    foreach (var m in summary?.Messages ?? Enumerable.Empty<string>()) Messages.Add(m);
                    Status = "Validation complete.";
                }
            });
        }

        private void SyncCobie()
        {
            _dispatcher.Raise(new RevitRequest { Id = RevitRequestId.SyncCobieFromIfc, Callback = r =>
            {
                var sync = r.SyncResult;
                var summary = sync?.Logs?.Where(x => x.Severity == "Info").Take(5).Select(x => x.Message) ?? Enumerable.Empty<string>();
                Status = $"COBie sync done. Instance updated: {sync?.InstancesUpdated ?? 0}, skipped: {sync?.InstancesSkipped ?? 0}, failed: {sync?.InstancesFailed ?? 0}. Type updated: {sync?.TypesUpdated ?? 0}, skipped: {sync?.TypesSkipped ?? 0}, failed: {sync?.TypesFailed ?? 0}. {string.Join(" | ", summary)}";
            }});
        }

        private void Export()
        {
            var d = new SaveFileDialog { Filter = "Text|*.txt", FileName = "DfE_ValidationReport.txt" };
            if (d.ShowDialog() != true) return;
            var lines = new[]
            {
                "Setup: " + SetupReadiness,
                "Naming: " + NamingCompleteness,
                "Header: " + HeaderCompleteness,
                "Space/Zone: " + SpaceZoneCompleteness,
                "Messages:",
            }.Concat(Messages);
            File.WriteAllLines(d.FileName, lines);
        }
    }
}
