$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# The worker build, locked production dependencies and assets are assembled by the PreparePayload target
# in src\CodeShield.Mcp, the same pipeline that produces the .NET tool package.
$out = Join-Path $root 'artifacts\codeshield'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\CodeShield.Mcp') -c Release -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host "Installed build: $out"
Write-Host "Server: dotnet $(Join-Path $out 'codeshield.dll')"
