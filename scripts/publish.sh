#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project_root"

self_contained=false
variant=framework-dependent
if [[ "${1:-}" == "--self-contained" && "$#" == 1 ]]; then
  self_contained=true
  variant=self-contained
elif [[ "$#" != 0 ]]; then
  echo "Usage: bash scripts/publish.sh [--self-contained]" >&2
  exit 2
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Install the .NET 10 SDK before building: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 1
fi

dotnet run --project tests/BatteryCharge.Checks/BatteryCharge.Checks.csproj -c Release
dotnet publish src/BatteryCharge.App/BatteryCharge.App.csproj \
  -c Release -r win-x64 --self-contained "$self_contained" \
  -p:EnableWindowsTargeting=true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishTrimmed=false \
  -p:DebugType=embedded \
  -o "artifacts/win-x64/$variant"

echo "Windows application: $project_root/artifacts/win-x64/$variant/BatteryCharge.exe"
