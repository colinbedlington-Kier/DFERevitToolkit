# DfE IFC Namer – Revit 2024 Install (Locked-down target machine)

Deployment layout (DiRoots style):

- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer.addin`
- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\DfEIfcNamer.dll`
- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\Resources\...`

## Build/package on a build machine
Use a machine with Visual Studio Build Tools and Revit API DLLs available (`lib\revit2024` or `REVIT_2024_API_PATH`):

```powershell
./build/package.ps1
```

This creates:
- `dist\Revit2024\AllUsers\`
- `dist\DfEIfcNamer-Revit2024-AllUsers.zip`

## Install on locked-down target machine (no Visual Studio required)
1. Download artifact zip (`DfEIfcNamer-Revit2024-AllUsers.zip`).
2. Extract the zip.
3. Copy `DfEIfcNamer.addin` to:
   - `C:\ProgramData\Autodesk\Revit\Addins\2024\`
4. Copy the folder `DfEIfcNamer` to:
   - `C:\ProgramData\Autodesk\Revit\Addins\2024\`
5. Restart Revit 2024.

## Uninstall
Delete both:
- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer.addin`
- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\`
