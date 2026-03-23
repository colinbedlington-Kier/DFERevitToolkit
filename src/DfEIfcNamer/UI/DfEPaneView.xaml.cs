using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using DfEIfcNamer.ExternalEvents;
using DfEIfcNamer.ViewModels;
using Microsoft.Win32;
using System.Text.Json;

namespace DfEIfcNamer.UI
{
    public partial class DfEFloatingWindow : Window
    {
        private bool _allowClose;

        public DfEFloatingWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => RequestDiagnosticsState();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnClosing(e);
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        private void RunFullDiagnostics_Click(object sender, RoutedEventArgs e) => RunDiagnosticsRequest(RevitRequestId.RunFullDiagnostics);
        private void CheckSharedParameterFile_Click(object sender, RoutedEventArgs e) => RunDiagnosticsRequest(RevitRequestId.CheckSharedParameterFile);
        private void CheckExpectedDefinitions_Click(object sender, RoutedEventArgs e) => RunDiagnosticsRequest(RevitRequestId.CheckExpectedDefinitions);
        private void CheckCategoryBindings_Click(object sender, RoutedEventArgs e) => RunDiagnosticsRequest(RevitRequestId.CheckCategoryBindings);
        private void TestSingleParameterBind_Click(object sender, RoutedEventArgs e) => RunDiagnosticsRequest(RevitRequestId.TestSingleParameterBind, "IFCName");
        private void ClearLog_Click(object sender, RoutedEventArgs e) => RunDiagnosticsRequest(RevitRequestId.ClearDiagnostics);

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            RequestDiagnosticsState(state =>
            {
                var summary = state?.Summary;
                var header =
                    "=== DfE IFC Namer Diagnostics Summary ===\n" +
                    "Document title: " + (summary?.DocumentTitle ?? "n/a") + "\n" +
                    "Revit version: " + (summary?.RevitVersion ?? "n/a") + "\n" +
                    "Active project name: " + (summary?.ActiveProjectName ?? "n/a") + "\n" +
                    "Shared parameter path: " + (summary?.SharedParameterPath ?? "n/a") + "\n" +
                    "Total groups: " + (summary?.GroupCount ?? 0) + "\n" +
                    "Total definitions: " + (summary?.DefinitionCount ?? 0) + "\n" +
                    "Total expected parameters: " + (summary?.TotalExpectedParameters ?? 0) + "\n" +
                    "Total parameters found: " + (summary?.TotalParametersFound ?? 0) + "\n" +
                    "Total insert successes: " + (summary?.TotalInsertSuccesses ?? 0) + "\n" +
                    "Total reinsert successes: " + (summary?.TotalReInsertSuccesses ?? 0) + "\n" +
                    "Total verified: " + (summary?.TotalVerified ?? 0) + "\n" +
                    "IFC classes loaded: " + (summary?.IfcClassesLoaded ?? 0) + "\n" +
                    "IFC predefined types loaded: " + (summary?.IfcPredefinedTypesLoaded ?? 0) + "\n" +
                    "Invalid IFC metadata count: " + (summary?.InvalidIfcMetadataCount ?? 0) + "\n" +
                    "========================================\n\n";
                Clipboard.SetText(header + (state?.PlainTextLog ?? string.Empty));
            });
        }

        private void ExportTxt_Click(object sender, RoutedEventArgs e)
        {
            RequestDiagnosticsState(state =>
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt",
                    FileName = "DfEIfcNamer_Diagnostics_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"
                };
                if (dialog.ShowDialog() == true)
                {
                    var summary = state?.Summary;
                    var payload =
                        "=== DfE IFC Namer Diagnostics Summary ===\n" +
                        "Document title: " + (summary?.DocumentTitle ?? "n/a") + "\n" +
                        "Revit version: " + (summary?.RevitVersion ?? "n/a") + "\n" +
                        "Active project name: " + (summary?.ActiveProjectName ?? "n/a") + "\n" +
                        "Shared parameter path: " + (summary?.SharedParameterPath ?? "n/a") + "\n" +
                        "Total groups: " + (summary?.GroupCount ?? 0) + "\n" +
                        "Total definitions: " + (summary?.DefinitionCount ?? 0) + "\n" +
                        "Total expected parameters: " + (summary?.TotalExpectedParameters ?? 0) + "\n" +
                        "Total parameters found: " + (summary?.TotalParametersFound ?? 0) + "\n" +
                        "Total insert successes: " + (summary?.TotalInsertSuccesses ?? 0) + "\n" +
                        "Total reinsert successes: " + (summary?.TotalReInsertSuccesses ?? 0) + "\n" +
                        "Total verified: " + (summary?.TotalVerified ?? 0) + "\n" +
                        "IFC classes loaded: " + (summary?.IfcClassesLoaded ?? 0) + "\n" +
                        "IFC predefined types loaded: " + (summary?.IfcPredefinedTypesLoaded ?? 0) + "\n" +
                        "Invalid IFC metadata count: " + (summary?.InvalidIfcMetadataCount ?? 0) + "\n" +
                        "========================================\n\n" +
                        (state?.PlainTextLog ?? string.Empty);
                    File.WriteAllText(dialog.FileName, payload);
                }
            });
        }

        private void ExportJson_Click(object sender, RoutedEventArgs e)
        {
            RequestDiagnosticsState(state =>
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = "DfEIfcNamer_Diagnostics_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"
                };
                if (dialog.ShowDialog() == true)
                {
                    var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
                }
            });
        }

        private void RunDiagnosticsRequest(RevitRequestId id, string parameterName = null)
        {
            var dispatcher = ResolveDispatcher();
            if (dispatcher == null)
            {
                return;
            }

            var categoryIds = (DataContext as MainViewModel)?.Categories
                .Where(c => c.IsSelected)
                .Select(c => new Autodesk.Revit.DB.ElementId(c.Id))
                .ToList();

            dispatcher.Raise(new RevitRequest
            {
                Id = id,
                CategoryIds = categoryIds,
                ParameterName = parameterName,
                Callback = response =>
                {
                    if (!string.IsNullOrWhiteSpace(response.Error))
                    {
                        DiagnosticsLogTextBox.Text = response.Error;
                        return;
                    }

                    ApplyDiagnosticsState(response.DiagnosticsState);
                }
            });
        }

        private void RequestDiagnosticsState(System.Action<Models.DiagnosticsState> callback = null)
        {
            var dispatcher = ResolveDispatcher();
            if (dispatcher == null)
            {
                return;
            }

            dispatcher.Raise(new RevitRequest
            {
                Id = RevitRequestId.GetDiagnosticsState,
                Callback = response =>
                {
                    ApplyDiagnosticsState(response.DiagnosticsState);
                    callback?.Invoke(response.DiagnosticsState);
                }
            });
        }

        private void ApplyDiagnosticsState(Models.DiagnosticsState state)
        {
            Dispatcher.Invoke(() =>
            {
                DiagnosticsLogTextBox.Text = state?.PlainTextLog ?? string.Empty;
                var summary = state?.Summary;
                DiagSharedPathText.Text = "Shared parameter path: " + (summary?.SharedParameterPath ?? "n/a");
                DiagFileExistsText.Text = "File exists?: " + (summary == null ? "n/a" : (summary.SharedParameterFileExists ? "yes" : "no"));
                DiagOpenSpfText.Text = "OpenSharedParameterFile success?: " + (summary == null ? "n/a" : (summary.OpenSharedParameterFileSucceeded ? "yes" : "no"));
                DiagGroupCountText.Text = "Group count: " + (summary?.GroupCount ?? 0);
                DiagDefinitionCountText.Text = "Definition count: " + (summary?.DefinitionCount ?? 0);
                DiagLastRunText.Text = "Last run: " + (summary?.LastRunTimeUtc?.ToLocalTime().ToString("g") ?? "n/a");
            }, DispatcherPriority.Background);
        }

        private RevitRequestDispatcher ResolveDispatcher()
        {
            var vm = DataContext;
            if (vm == null)
            {
                return null;
            }

            var field = vm.GetType().GetField("_dispatcher", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return field?.GetValue(vm) as RevitRequestDispatcher;
        }
    }
}
