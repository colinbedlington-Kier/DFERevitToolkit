param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$csproj = Join-Path $RepoRoot "src\DfEIfcNamer\DfEIfcNamer.csproj"
$dllPath = Join-Path $RepoRoot "src\DfEIfcNamer\bin\Release\DfEIfcNamer.dll"

if (-not (Test-Path $csproj)) {
    throw "Project file not found: $csproj"
}

Write-Host "[DfE IFC Namer] Building Release: $csproj"

$buildSucceeded = $false

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd) {
    Write-Host "[DfE IFC Namer] Trying dotnet build..."
    & $dotnetCmd.Source build $csproj -c Release
    if ($LASTEXITCODE -eq 0) {
        $buildSucceeded = $true
        Write-Host "[DfE IFC Namer] dotnet build succeeded."
    }
    else {
        Write-Warning "dotnet build failed. Will try MSBuild fallback."
    }
}
else {
    Write-Warning "dotnet CLI not found. Will try MSBuild fallback."
}

if (-not $buildSucceeded) {
    $msbuildCandidates = @(
        (Join-Path $Env:ProgramFiles "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
        (Join-Path $Env:ProgramFiles "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe")
    )

    $msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $msbuild) {
        throw "No build tools found. Install .NET SDK or Visual Studio 2022 Build Tools/Community with MSBuild."
    }

    Write-Host "[DfE IFC Namer] Trying MSBuild fallback: $msbuild"
    & $msbuild $csproj /t:Build /p:Configuration=Release /p:Platform="Any CPU"
    if ($LASTEXITCODE -eq 0) {
        $buildSucceeded = $true
        Write-Host "[DfE IFC Namer] MSBuild succeeded."
    }
}

if (-not $buildSucceeded -or -not (Test-Path $dllPath)) {
    throw "Build failed or expected DLL missing: $dllPath"
}

Write-Host "[DfE IFC Namer] Build complete. DLL: $dllPath"
