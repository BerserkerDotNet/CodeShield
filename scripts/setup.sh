#!/usr/bin/env sh
set -eu
root="$(cd "$(dirname "$0")/.." && pwd)"

# The TypeScript worker build, its locked production dependencies, the Kotlin worker jar (built with the Gradle wrapper,
# which needs a JDK 17+ on the producer machine) and the assets are assembled by the PreparePayload target in
# src/CodeShield.Mcp, the same pipeline that produces the .NET tool package.
out="$root/artifacts/codeshield"
rm -rf "$out"
dotnet publish "$root/src/CodeShield.Mcp" -c Release -o "$out"

echo "Installed build: $out"
echo "Server: dotnet $out/codeshield.dll"
