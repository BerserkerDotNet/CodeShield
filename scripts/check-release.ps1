#requires -Version 5.1
# Validates that every place carrying the product version agrees, that CHANGELOG.md documents it, and (optionally)
# that a release tag matches. Prints the version's changelog section with -NotesPath. Used by CI, the release workflow
# and ReleaseContractTests, so there is one definition of "release-consistent".
param(
    [string] $Tag,
    [string] $NotesPath,
    [string] $RepoRoot
)
$ErrorActionPreference = 'Stop'
$root = if ($RepoRoot) { $RepoRoot } else { Split-Path -Parent $PSScriptRoot }

function Read-Json([string] $relative) { Get-Content (Join-Path $root $relative) -Raw | ConvertFrom-Json }

$project = Get-Content (Join-Path $root 'src/CodeShield.Mcp/CodeShield.Mcp.csproj') -Raw
if ($project -notmatch '<Version>(?<v>[^<]+)</Version>') { throw 'CodeShield.Mcp.csproj has no <Version>.' }
$version = $Matches['v']
$identifier = '(0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)'
$semver = "^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-$identifier(\.$identifier)*)?$"
if ($version -cnotmatch $semver) { throw "Version '$version' is not Semantic Versioning 2.0.0 (build metadata is not supported)." }

$errors = @()
foreach ($manifest in 'integrations/codeshield/plugin.json', 'integrations/codeshield/.claude-plugin/plugin.json') {
    $declared = (Read-Json $manifest).version
    if ($declared -ne $version) { $errors += "$manifest declares $declared, expected $version." }
}

if ($Tag -and $Tag -ne "v$version") { $errors += "Tag '$Tag' does not match product version v$version." }

$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
$section = [regex]::Match($changelog, "(?ms)^## \[$([regex]::Escape($version))\][^\r\n]*\r?\n(?<body>.*?)(?=^## \[|\z)")
if ($Tag -and -not $section.Success) { $errors += "CHANGELOG.md has no '## [$version]' section." }
if ($Tag -and $section.Success -and [string]::IsNullOrWhiteSpace($section.Groups['body'].Value)) { $errors += "CHANGELOG.md section [$version] is empty." }
if (-not $Tag -and -not $section.Success -and $changelog -notmatch '(?m)^## \[Unreleased\]') { $errors += "CHANGELOG.md needs an [Unreleased] or [$version] section." }

if ($errors) { throw ($errors -join [Environment]::NewLine) }
if ($NotesPath) { [IO.File]::WriteAllText($NotesPath, $section.Groups['body'].Value.Trim() + "`n", (New-Object Text.UTF8Encoding $false)) }
Write-Output $version
