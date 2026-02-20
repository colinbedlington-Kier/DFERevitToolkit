param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\")).Path
)

$ErrorActionPreference = "Stop"

$buildOutput = Join-Path $RepoRoot "src\DfEIfcNamer\bin\Release"
$dllSource = Join-Path $buildOutput "DfEIfcNamer.dll"

if (-not (Test-Path $dllSource)) {
    Write-Host "[DfE IFC Namer] Release DLL not found. Attempting auto-build..."
    $buildScript = Join-Path $RepoRoot "build\build-release.ps1"

    if (Test-Path $buildScript) {
        try {
            & $buildScript -RepoRoot $RepoRoot
        }
        catch {
            throw "No build tools found. Build on another machine or use the packaged zip from /dist. Details: $($_.Exception.Message)"
        }
    }
    else {
        throw "No build tools found. Build on another machine or use the packaged zip from /dist."
    }
}

if (-not (Test-Path $dllSource)) {
    throw "No build tools found. Build on another machine or use the packaged zip from /dist."
}

$addinRoot = "C:\ProgramData\Autodesk\Revit\Addins\2024"
$pluginFolder = Join-Path $addinRoot "DfEIfcNamer"
$resourcesFolder = Join-Path $pluginFolder "Resources"
$addinPath = Join-Path $addinRoot "DfEIfcNamer.addin"

New-Item -ItemType Directory -Force -Path $pluginFolder | Out-Null
New-Item -ItemType Directory -Force -Path $resourcesFolder | Out-Null

Copy-Item (Join-Path $buildOutput "*.dll") $pluginFolder -Force
Copy-Item (Join-Path $buildOutput "*.pdb") $pluginFolder -Force -ErrorAction SilentlyContinue

$resourceSource = Join-Path $RepoRoot "src\DfEIfcNamer\Resources"
Copy-Item (Join-Path $resourceSource "ifc2x3_entity_predefinedtypes.json") $resourcesFolder -Force
Copy-Item (Join-Path $resourceSource "classification_slots.json") $resourcesFolder -Force
Copy-Item (Join-Path $resourceSource "DfE_IfcNamer_SharedParameters.txt") $resourcesFolder -Force

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

$sharedParamInstalled = Join-Path $resourcesFolder "DfE_IfcNamer_SharedParameters.txt"

if (-not (Test-Path $assemblyPath)) {
    throw "Install failed: DLL not found at $assemblyPath"
}

if (-not (Test-Path $sharedParamInstalled)) {
    throw "Install failed: Shared parameter file not found at $sharedParamInstalled"
}

Write-Host "Install complete"
Write-Host "Addin file: $addinPath"
Write-Host "Plugin folder: $pluginFolder"
Write-Host "DLL: $assemblyPath"
Write-Host "Shared parameters: $sharedParamInstalled"
