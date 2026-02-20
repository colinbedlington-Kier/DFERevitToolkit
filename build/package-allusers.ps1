param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$buildScript = Join-Path $RepoRoot "build\build-release.ps1"
& $buildScript -RepoRoot $RepoRoot

$releaseOut = Join-Path $RepoRoot "src\DfEIfcNamer\bin\Release"
$dll = Join-Path $releaseOut "DfEIfcNamer.dll"
if (-not (Test-Path $dll)) {
    throw "Expected build output missing: $dll"
}

$distRoot = Join-Path $RepoRoot "dist\Revit2024"
$packageRoot = Join-Path $distRoot "AllUsers"
$pluginRoot = Join-Path $packageRoot "DfEIfcNamer"
$pluginResources = Join-Path $pluginRoot "Resources"

if (Test-Path $packageRoot) {
    Remove-Item -Recurse -Force $packageRoot
}

New-Item -ItemType Directory -Force -Path $pluginResources | Out-Null

Copy-Item (Join-Path $releaseOut "*.dll") $pluginRoot -Force
Copy-Item (Join-Path $releaseOut "*.pdb") $pluginRoot -Force -ErrorAction SilentlyContinue

$resourceSource = Join-Path $RepoRoot "src\DfEIfcNamer\Resources"
Copy-Item (Join-Path $resourceSource "ifc2x3_entity_predefinedtypes.json") $pluginResources -Force
Copy-Item (Join-Path $resourceSource "classification_slots.json") $pluginResources -Force
Copy-Item (Join-Path $resourceSource "DfE_IfcNamer_SharedParameters.txt") $pluginResources -Force

$addinPath = Join-Path $packageRoot "DfEIfcNamer.addin"
$assemblyPath = "C:\ProgramData\Autodesk\Revit\Addins\2024\DfEIfcNamer\DfEIfcNamer.dll"
$addinXml = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>DfE IFC Namer</Name>
    <Assembly>$assemblyPath</Assembly>
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

Write-Host "[DfE IFC Namer] Package complete"
Write-Host "Folder: $packageRoot"
Write-Host "Zip: $zipPath"
