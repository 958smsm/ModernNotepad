$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Get-ChildItem $root -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin', 'obj', 'TestResults') } |
    Sort-Object FullName -Descending |
    Remove-Item -Recurse -Force

$artifacts = Join-Path $root 'artifacts'
if (Test-Path $artifacts) {
    Remove-Item $artifacts -Recurse -Force
}
