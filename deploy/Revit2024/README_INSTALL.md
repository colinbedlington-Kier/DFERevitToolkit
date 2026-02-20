# DfE IFC Namer – Revit 2024 (All Users / DiRoots-style)

This repo supports a DiRoots-style deployment layout:

- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer.addin`
- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\DfEIfcNamer.dll`
- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\Resources\*.json`
- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\Resources\DfE_IfcNamer_SharedParameters.txt`

## 3-step quick guide

1. **Package (recommended):**
   ```powershell
   ./build/package-allusers.ps1
   ```
   Produces:
   - `dist\Revit2024\AllUsers\...`
   - `dist\Revit2024\DfEIfcNamer-Revit2024-AllUsers.zip`

2. **Or install directly (auto-build if possible):**
   ```powershell
   ./deploy/Revit2024/install-allusers.ps1
   ```
   If `src\DfEIfcNamer\bin\Release\DfEIfcNamer.dll` is missing, the script attempts `build\build-release.ps1` automatically.

3. **Restart Revit 2024** and confirm the **DfE IFC Namer** ribbon panel/button appears.

## Build details
`build/build-release.ps1` tries:
1. `dotnet build src\DfEIfcNamer\DfEIfcNamer.csproj -c Release`
2. MSBuild fallback at common VS2022 paths.

If neither is available, build on another machine or use a packaged zip from `dist`.

## Uninstall
Delete both:
- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer.addin`
- `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\`

## Troubleshooting

### Add-in assembly not found
- Check `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\DfEIfcNamer.dll` exists.
- Open `DfEIfcNamer.addin` and verify `<Assembly>` matches the absolute ProgramData DLL path.

### Shared parameter file not found
- Verify:
  `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\Resources\DfE_IfcNamer_SharedParameters.txt`

### Revit API references missing during build
- Ensure Revit 2024 is installed and these exist:
  - `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll`
  - `C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll`
