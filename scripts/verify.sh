#!/usr/bin/env bash
set -euo pipefail

if ! command -v dotnet >/dev/null 2>&1; then
  echo "The .NET 10 SDK is required. Install it before running this script." >&2
  exit 127
fi

dotnet --version
dotnet restore scada.slnx
dotnet build scada.slnx --configuration Release --no-restore
dotnet test scada.slnx --configuration Release --no-build

if command -v node >/dev/null 2>&1; then
  node --check src/Api/wwwroot/app.js
fi
