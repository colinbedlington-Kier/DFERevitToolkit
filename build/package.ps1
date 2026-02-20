param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$csproj = Join-Path $RepoRoot "src\DfEIfcNamer\DfEIfcNamer.csproj"
$releaseOut = Join-Path $RepoRoot "src\DfEIfcNamer\bin\$Configuration"
$dllPath = Join-Path $releaseOut "DfEIfcNamer.dll"

$msbuildCandidates = @(
    (Join-Path $Env:ProgramFiles "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
    (Join-Path $Env:ProgramFiles "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
    (Join-Path $Env:ProgramFiles "Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
    (Join-Path $Env:ProgramFiles "Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe")
)

$msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msbuild) {
    throw "MSBuild.exe not found. Install Visual Studio 2022 Build Tools on the build machine."
}

Write-Host "[DfE IFC Namer] Building with MSBuild: $msbuild"
& $msbuild $csproj /t:Build /p:Configuration=$Configuration /p:Platform="Any CPU"
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $dllPath)) {
    throw "Build succeeded but expected DLL missing: $dllPath"
}

$distRoot = Join-Path $RepoRoot "dist"
$distRevit = Join-Path $distRoot "Revit2024"
$packageRoot = Join-Path $distRevit "AllUsers"
$pluginRoot = Join-Path $packageRoot "DfEIfcNamer"
$resourceRoot = Join-Path $pluginRoot "Resources"

if (Test-Path $packageRoot) {
    Remove-Item -Recurse -Force $packageRoot
}

New-Item -ItemType Directory -Force -Path $resourceRoot | Out-Null

Copy-Item (Join-Path $releaseOut "*.dll") $pluginRoot -Force
Copy-Item (Join-Path $releaseOut "*.pdb") $pluginRoot -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $releaseOut "*.config") $pluginRoot -Force -ErrorAction SilentlyContinue

$sourceResources = Join-Path $RepoRoot "src\DfEIfcNamer\Resources"
Copy-Item (Join-Path $sourceResources "ifc2x3_entity_predefinedtypes.json") $resourceRoot -Force
Copy-Item (Join-Path $sourceResources "classification_slots.json") $resourceRoot -Force
Copy-Item (Join-Path $sourceResources "DfE_IfcNamer_SharedParameters.txt") $resourceRoot -Force

$addinPath = Join-Path $packageRoot "DfEIfcNamer.addin"
$addinXml = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>DfE IFC Namer</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\DfEIfcNamer.dll</Assembly>
    <AddInId>8F9E6D6A-9EA8-4AB4-95AF-383843A51621</AddInId>
    <FullClassName>DfEIfcNamer.App.DfEApplication</FullClassName>
    <VendorId>DFE</VendorId>
    <VendorDescription>Department for Education IFC naming tooling.</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
Set-Content -Path $addinPath -Value $addinXml -Encoding UTF8

$zipPath = Join-Path $distRoot "DfEIfcNamer-Revit2024-AllUsers.zip"
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force

Write-Host "[DfE IFC Namer] Package ready"
Write-Host "Package folder: $packageRoot"
Write-Host "Zip: $zipPath"
