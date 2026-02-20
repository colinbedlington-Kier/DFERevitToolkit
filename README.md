# DfE IFC Namer (Revit 2024)

Production-oriented Revit 2024 add-in for generating DfE-compliant IFC2x3 naming outputs **only through IFC/classification parameters**.

## Key behavior
- Uses a WPF Dockable Pane (MVVM).
- Executes Revit writes through ExternalEvent.
- Bootstraps shared parameters on first run.
- Writes only:
  - `IfcName[Type]`, `IfcDescription[Type]`
  - `IfcName`, `IfcDescription`
  - `Classification` ... `Classification(9)`
  - `IFC Export As`, `IFC Export Type`, `IFC Predefined Type`
  - Project Info: `DfE_ProjectInfoJson`, `DfE_NamingCounters`
- Never touches family/type names, marks, or identity fields.

## Build
- Revit 2024
- .NET Framework 4.8
- Visual Studio 2022

Revit references are expected at:
- `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll`
- `C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll`

## Manual deployment
1. Build `src/DfEIfcNamer` in Release.
2. Copy `DfEIfcNamer.dll` to `C:\DfEIfcNamer\`.
3. Copy `addin/DfEIfcNamer.addin` to:
   - `%AppData%\Autodesk\Revit\Addins\2024\`
4. Ensure `<Assembly>` path in `.addin` matches the DLL location.

## JSON resources
Editable without recompilation:
- `src/DfEIfcNamer/Resources/ifc2x3_entity_predefinedtypes.json`
- `src/DfEIfcNamer/Resources/classification_slots.json`
