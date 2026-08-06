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
    dotnet build ModernNotepad.sln -c $Configuration --no-restore
}
finally {
    Pop-Location
}
