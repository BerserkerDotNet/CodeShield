$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Push-Location (Join-Path $root 'worker')
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'npm run build failed' }
}
finally { Pop-Location }

$out = Join-Path $root 'artifacts\codeshield'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\CodeShield.Mcp') -c Release -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# Self-contained layout: host, analyzers and worker (with production node_modules) side by side.
$worker = Join-Path $out 'worker'
New-Item -ItemType Directory $worker -Force | Out-Null
Copy-Item (Join-Path $root 'worker\dist') (Join-Path $worker 'dist') -Recurse
Copy-Item (Join-Path $root 'worker\package.json'), (Join-Path $root 'worker\package-lock.json') $worker
Push-Location $worker
try {
    npm ci --omit=dev
    if ($LASTEXITCODE -ne 0) { throw 'npm ci --omit=dev failed' }
}
finally { Pop-Location }

Copy-Item (Join-Path $root 'docs\schemas') (Join-Path $out 'schemas') -Recurse
Copy-Item (Join-Path $root 'docs\release-manifest.json') $out
Copy-Item (Join-Path $root 'docs\agent-instructions') (Join-Path $out 'agent-instructions') -Recurse

Write-Host "Installed build: $out"
Write-Host "Server: dotnet $(Join-Path $out 'codeshield.dll')"
