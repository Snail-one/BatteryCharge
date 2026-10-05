param([switch]$SelfContained)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$variant = if ($SelfContained) { "self-contained" } else { "framework-dependent" }
$bundleRuntime = if ($SelfContained) { "true" } else { "false" }
$output = Join-Path $projectRoot "artifacts/win-x64/$variant"

Push-Location $projectRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
    }

    & dotnet run --project tests/BatteryCharge.Checks/BatteryCharge.Checks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Behavior checks failed ($LASTEXITCODE)." }

    & dotnet publish src/BatteryCharge.App/BatteryCharge.App.csproj `
        -c Release -r win-x64 --self-contained $bundleRuntime `
        -p:EnableWindowsTargeting=true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false `
        -p:DebugType=embedded `
        -o $output
    if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }

    Write-Host "Windows application: $(Join-Path $output 'BatteryCharge.exe')"
}
finally {
    Pop-Location
}
