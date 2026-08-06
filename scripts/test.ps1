[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet restore ModernNotepad.sln
    dotnet test tests/ModernNotepad.Tests/ModernNotepad.Tests.csproj `
        -c $Configuration `
        --no-restore `
        --logger "console;verbosity=normal"
}
finally {
    Pop-Location
}
