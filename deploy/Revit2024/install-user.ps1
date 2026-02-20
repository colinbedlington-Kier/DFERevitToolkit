param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\")).Path
)

$buildOutput = Join-Path $RepoRoot "src\DfEIfcNamer\bin\Release"
$dllSource = Join-Path $buildOutput "DfEIfcNamer.dll"

if (-not (Test-Path $dllSource)) {
    throw "Build output not found: $dllSource. Build Release first."
}

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$pluginFolder = Join-Path $addinRoot "DfEIfcNamer"
$addinPath = Join-Path $addinRoot "DfEIfcNamer.addin"

New-Item -ItemType Directory -Force -Path $pluginFolder | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $pluginFolder "Resources") | Out-Null

Copy-Item (Join-Path $buildOutput "*.dll") $pluginFolder -Force
Copy-Item (Join-Path $buildOutput "*.pdb") $pluginFolder -Force -ErrorAction SilentlyContinue

if (Test-Path (Join-Path $buildOutput "Resources")) {
    Copy-Item (Join-Path $buildOutput "Resources\*") (Join-Path $pluginFolder "Resources") -Recurse -Force
}

$resourceSource = Join-Path $RepoRoot "src\DfEIfcNamer\Resources"
Copy-Item (Join-Path $resourceSource "*.json") (Join-Path $pluginFolder "Resources") -Force
Copy-Item (Join-Path $resourceSource "DfE_IfcNamer_SharedParameters.txt") $pluginFolder -Force
Copy-Item (Join-Path $resourceSource "DfE_IfcNamer_SharedParameters.txt") (Join-Path $pluginFolder "Resources") -Force

$assemblyPath = Join-Path $pluginFolder "DfEIfcNamer.dll"
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

Write-Host "Installed DfE IFC Namer (User scope)"
Write-Host "Addin file: $addinPath"
Write-Host "Plugin folder: $pluginFolder"
Write-Host "Assembly: $assemblyPath"
