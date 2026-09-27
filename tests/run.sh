#!/usr/bin/env bash
# Offline checks for the VFX Toolkit package (needs the .NET 8 SDK):
#  1. the package compiles with and without MCP for Unity / URP present (warnings are errors)
#  2. particle recipes map onto real Unity module properties, and bad recipes are rejected clearly
set -euo pipefail
cd "$(dirname "$0")"
./setup.sh
# MSBuild needs ";" escaped as %3B inside -p values.
for defines in "VFXTOOLKIT_MCP%3BVFXTOOLKIT_URP" "NONE"; do
  echo "== compile package (defines: $defines)"
  dotnet build PackageCompile -nologo -v q "-p:Defines=$defines" | grep -E "error|Build succeeded" | sort -u
done
echo "== recipe mapping"
dotnet run --project RecipeMapping -nologo -v q
