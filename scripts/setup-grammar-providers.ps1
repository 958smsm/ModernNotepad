param(
    [string]$Python = $(if ($env:MODERNNOTEPAD_PYTHON) { $env:MODERNNOTEPAD_PYTHON } else { "python" })
)

$ErrorActionPreference = "Stop"

& $Python -m pip install --upgrade spacy nltk
& $Python -m spacy download en_core_web_sm
& $Python -m nltk.downloader averaged_perceptron_tagger_eng

Write-Host "Modern Notepad Python grammar providers are ready."
Write-Host "Using Python: $Python"
