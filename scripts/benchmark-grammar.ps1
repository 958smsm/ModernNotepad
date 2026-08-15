[CmdletBinding()]
param(
    [double]$MinimumAccuracy = 0.90,
    [double]$MinimumCoverage = 0.98,
    [double]$MinimumTokensPerSecond = 25000,
    [switch]$ShowErrors
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "benchmarks\ModernNotepad.GrammarBenchmark\ModernNotepad.GrammarBenchmark.csproj"
$benchmarkArguments = @(
    "run",
    "--project", $project,
    "--configuration", "Release",
    "--",
    "--minimum-accuracy", $MinimumAccuracy.ToString(
        [System.Globalization.CultureInfo]::InvariantCulture),
    "--minimum-coverage", $MinimumCoverage.ToString(
        [System.Globalization.CultureInfo]::InvariantCulture),
    "--minimum-throughput", $MinimumTokensPerSecond.ToString(
        [System.Globalization.CultureInfo]::InvariantCulture)
)

if ($ShowErrors)
{
    $benchmarkArguments += "--show-errors"
    $benchmarkArguments += "25"
}

& dotnet @benchmarkArguments
exit $LASTEXITCODE
