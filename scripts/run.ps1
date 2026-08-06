[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Files
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $arguments = @('run', '--project', 'src/ModernNotepad.App/ModernNotepad.App.csproj')
    if ($Files.Count -gt 0) {
        $arguments += '--'
        $arguments += $Files
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Modern Notepad exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
