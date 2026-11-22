#!/usr/bin/env sh
set -eu
root="$(cd "$(dirname "$0")/.." && pwd)"

(cd "$root/worker" && npm ci && npm run build)
out="$root/artifacts/codeshield"
rm -rf "$out"
dotnet publish "$root/src/CodeShield.Mcp" -c Release -o "$out"

mkdir -p "$out/worker"
cp -R "$root/worker/dist" "$out/worker/dist"
cp "$root/worker/package.json" "$root/worker/package-lock.json" "$out/worker/"
(cd "$out/worker" && npm ci --omit=dev)
cp -R "$root/docs/schemas" "$out/schemas"
cp "$root/docs/release-manifest.json" "$out/"
cp -R "$root/docs/agent-instructions" "$out/agent-instructions"

echo "Installed build: $out"
echo "Server: dotnet $out/codeshield.dll"
