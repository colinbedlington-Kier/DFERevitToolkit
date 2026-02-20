# DfE IFC Namer – Revit 2024 Installation

## 1) Build (Release)
1. Open `src/DfEIfcNamer/DfEIfcNamer.csproj` in Visual Studio 2022.
2. Ensure Revit 2024 API references resolve:
   - `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll`
   - `C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll`
3. Build configuration: **Release | Any CPU**.
4. Expected output DLL:
   - `src\DfEIfcNamer\bin\Release\DfEIfcNamer.dll`

## 2) Install (User, no admin)
Run in PowerShell:

```powershell
./deploy/Revit2024/install-user.ps1
```

Installed layout:
- `.addin`:
  `%APPDATA%\Autodesk\Revit\Addins\2024\DfEIfcNamer.addin`
- plugin folder:
  `%APPDATA%\Autodesk\Revit\Addins\2024\DfEIfcNamer\`
  - `DfEIfcNamer.dll`
  - `DfEIfcNamer.pdb` (optional)
  - `Resources\...`
  - `DfE_IfcNamer_SharedParameters.txt`

## 3) Install (All Users, admin)
Run PowerShell as Administrator:

```powershell
./deploy/Revit2024/install-allusers.ps1
```

Installed layout:
- `.addin`:
  `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer.addin`
- plugin folder:
  `C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\`

## 4) Uninstall
Delete:
- the installed `.addin` file, and
- the installed `DfEIfcNamer` folder
from either user scope (`%APPDATA%`) or all-users scope (`C:\ProgramData`) depending on how installed.

## 5) Troubleshooting

### External Tools – Add-in Assembly Not Found
- Re-run install script and confirm reported `Assembly` path exists.
- Open installed `.addin` and verify `<Assembly>` points to `...\DfEIfcNamer\DfEIfcNamer.dll`.

### Shared parameter file not found
- Confirm this file exists in installed plugin folder:
  - `DfE_IfcNamer_SharedParameters.txt`
  - or `Resources\DfE_IfcNamer_SharedParameters.txt`
- Use ribbon command **DfEIfcNamer: Diagnostics** to inspect resolved path and existence.

### Revit API references missing
- Confirm Revit 2024 is installed and paths in `.csproj` exist.
- Re-open project and rebuild Release after fixing reference paths.
