#!/usr/bin/env sh
set -eu
root="$(cd "$(dirname "$0")/.." && pwd)"

# The worker build, locked production dependencies and assets are assembled by the PreparePayload target
# in src/CodeShield.Mcp, the same pipeline that produces the .NET tool package.
out="$root/artifacts/codeshield"
rm -rf "$out"
dotnet publish "$root/src/CodeShield.Mcp" -c Release -o "$out"

echo "Installed build: $out"
echo "Server: dotnet $out/codeshield.dll"
