[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/ModernNotepad.App/ModernNotepad.App.csproj'
$publishDir = Join-Path $root "artifacts/publish/$Runtime"
$version = '1.0.0'
$mode = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
$zipPath = Join-Path $root "artifacts/ModernNotepad-$version-$Runtime-$mode.zip"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item $publishDir -ItemType Directory -Force | Out-Null

$arguments = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(),
    '-p:PublishSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $publishDir
)

Push-Location $root
try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with code $LASTEXITCODE."
    }

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Published: $publishDir"
    Write-Host "Package:   $zipPath"
}
finally {
    Pop-Location
}
