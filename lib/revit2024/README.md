# Revit 2024 API DLLs (not committed)

To build `DfEIfcNamer` you must provide Autodesk Revit 2024 API assemblies:

- `RevitAPI.dll`
- `RevitAPIUI.dll`

## Option 1: Local repo folder (default)
Copy both DLLs into this folder:

- `lib/revit2024/RevitAPI.dll`
- `lib/revit2024/RevitAPIUI.dll`

The project file references this location by default.

## Option 2: Environment variable override
Set `REVIT_2024_API_PATH` to a folder containing both DLLs.

Example PowerShell:

```powershell
$env:REVIT_2024_API_PATH = 'D:\secure\revit2024-api'
```

## Licensing note
Autodesk binaries are subject to Autodesk licensing terms. Do not commit these DLLs to public repositories unless permitted.
