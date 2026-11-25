#requires -Version 5.1
# Packs the tool, installs exactly that package into a uniquely named temporary tool path using only the freshly built local
# feed, and runs every installed-package test from an unrelated working directory. Global tools, client profiles and any
# pre-existing artifacts are left alone; only paths created by this invocation are removed. The packed .nupkg is copied to
# artifacts/feed for the CI artifact upload.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$expectedInstalledTests = 3

function Invoke-Native([string] $What, [scriptblock] $Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = @(& $Command 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous
    $output | Write-Host
    if ($code -ne 0) { throw "$What failed with exit code $code." }
    return ($output -join "`n")
}

$version = (& (Join-Path $PSScriptRoot 'check-release.ps1') | Select-Object -Last 1).ToString().Trim()
$run = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$work = Join-Path ([IO.Path]::GetTempPath()) "codeshield verify $run"
$localFeed = Join-Path $work 'feed'
$toolPath = Join-Path $work 'tool path'
$sources = Join-Path $work 'nuget.config'
$packages = Join-Path $work 'packages'
$handoff = Join-Path $root 'artifacts/feed'
$savedPackages = $env:NUGET_PACKAGES
$savedInstalled = $env:CODESHIELD_INSTALLED_EXE

try {
    New-Item -ItemType Directory $localFeed, $toolPath, $packages | Out-Null
    Invoke-Native 'dotnet pack' { dotnet pack (Join-Path $root 'src/CodeShield.Mcp') -c Release -o $localFeed } | Out-Null
    $package = Join-Path $localFeed "CodeShield.Tool.$version.nupkg"
    if (-not (Test-Path $package)) { throw "Pack did not produce $package." }

    $config = "<?xml version=`"1.0`" encoding=`"utf-8`"?><configuration><packageSources><clear /><add key=`"local`" value=`"$localFeed`" /></packageSources></configuration>"
    [IO.File]::WriteAllText($sources, $config, (New-Object Text.UTF8Encoding $false))

    # Tool restore uses only the local feed and a private package cache, so no NuGet.org or cached package can be selected.
    $env:NUGET_PACKAGES = $packages
    Invoke-Native 'dotnet tool install' { dotnet tool install CodeShield.Tool --version $version --tool-path $toolPath --configfile $sources } | Out-Null
    $env:NUGET_PACKAGES = $savedPackages

    $exe = Join-Path $toolPath 'codeshield.exe'
    if (-not (Test-Path $exe)) { $exe = Join-Path $toolPath 'codeshield' }
    $reported = Invoke-Native 'codeshield --version' { & $exe --version }
    if ($reported -notmatch [regex]::Escape($version)) { throw "Installed tool reports '$reported', expected $version." }
    Invoke-Native 'codeshield doctor' { & $exe doctor } | Out-Null

    $env:CODESHIELD_INSTALLED_EXE = $exe
    $filter = 'FullyQualifiedName~InstalledToolTests|FullyQualifiedName~ReleaseContractTests.InstalledPackageCarries'
    $tests = Invoke-Native 'installed tests' { dotnet test (Join-Path $root 'tests/CodeShield.IntegrationTests') --filter $filter }
    if ($tests -notmatch "Passed:\s+$expectedInstalledTests,\s+Skipped:\s+0,\s+Total:\s+$expectedInstalledTests\b") {
        throw "Expected exactly $expectedInstalledTests installed tests to run and pass, none skipped."
    }

    New-Item -ItemType Directory $handoff -Force | Out-Null
    Copy-Item $package $handoff -Force
    Write-Host "Installed tool verified ($expectedInstalledTests installed tests executed): $exe"
}
finally {
    $env:NUGET_PACKAGES = $savedPackages
    $env:CODESHIELD_INSTALLED_EXE = $savedInstalled
    if ($work -and (Test-Path $work)) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}
