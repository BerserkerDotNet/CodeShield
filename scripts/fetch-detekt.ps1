#requires -Version 5.1
# Downloads a detekt-cli all-in-one jar from Maven Central into a directory outside the repository, verifies it against the
# SHA-256 pinned below and prints its path. With -ExportToGitHubEnv it also sets CODESHIELD_DETEKT for later CI steps.
# This is a test and CI convenience: CodeShield never downloads detekt on a user's machine; users install detekt-cli themselves.
param(
    [ValidateSet('1.23.0', '1.23.8')]
    [string] $Version = '1.23.8',
    [string] $Destination = [IO.Path]::GetTempPath(),
    [switch] $ExportToGitHubEnv
)
$ErrorActionPreference = 'Stop'

# The oldest supported release and the release CodeShield is developed against. Maven Central artifacts are immutable.
$pinned = @{
    '1.23.0' = '5e699cc9fc1664013d3d06ba4cfd8766c9fb8800f031405dc4c69df236161fda'
    '1.23.8' = '3afe89a11120303c73c9bdda3d8fe558dd9070a6937d27819ddc04b275381245'
}

$name = "detekt-cli-$Version-all.jar"
$jar = Join-Path $Destination $name
if (-not (Test-Path $jar) -or (Get-FileHash $jar -Algorithm SHA256).Hash -ne $pinned[$Version]) {
    New-Item -ItemType Directory $Destination -Force | Out-Null
    $partial = "$jar.partial"
    $previous = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest "https://repo1.maven.org/maven2/io/gitlab/arturbosch/detekt/detekt-cli/$Version/$name" -OutFile $partial -UseBasicParsing
    }
    finally {
        $ProgressPreference = $previous
    }

    $actual = (Get-FileHash $partial -Algorithm SHA256).Hash
    if ($actual -ne $pinned[$Version]) {
        Remove-Item $partial -Force
        throw "$name has SHA-256 $actual, expected $($pinned[$Version])."
    }

    Move-Item $partial $jar -Force
}

if ($ExportToGitHubEnv) {
    if (-not $env:GITHUB_ENV) { throw '-ExportToGitHubEnv needs GITHUB_ENV (GitHub Actions).' }
    "CODESHIELD_DETEKT=$jar" | Out-File $env:GITHUB_ENV -Append -Encoding utf8
}

Write-Output $jar
