#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "Building and running RocketWelder.SDK.Blazor.Sample..."
dotnet run --urls "http://localhost:5200"
