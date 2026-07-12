<#
.SYNOPSIS
Sets up the Piper local neural TTS tier for jtalk:
creates a venv under %APPDATA%\jtalk\piper, installs piper-tts, downloads voices.

.NOTES
Python is found via -PythonExe, then `py -3`, then `python` on PATH.
The daemon only ever runs the venv's piper.exe (which embeds its interpreter path),
so Python's PATH situation doesn't matter at runtime.
#>
param(
    [string]$PythonExe = "",
    [string[]]$Voices = @("en_GB-alan-medium", "en_GB-alba-medium"),
    [switch]$AcceptVoiceLicenses
)

$ErrorActionPreference = "Stop"

function Find-Python {
    param([string]$Preferred)
    $candidates = @()
    if ($Preferred) { $candidates += $Preferred }
    $candidates += "$env:LOCALAPPDATA\Programs\Python\Python314\python.exe"
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    foreach ($cmd in @("py", "python")) {
        $found = Get-Command $cmd -ErrorAction SilentlyContinue
        # The WindowsApps stub is not a real Python
        if ($found -and $found.Source -notlike "*WindowsApps*") {
            if ($cmd -eq "py") { return "py" }
            return $found.Source
        }
    }
    throw "No Python installation found. Install Python 3.9+ or pass -PythonExe."
}

$python = Find-Python -Preferred $PythonExe
$piperRoot = Join-Path $env:APPDATA "jtalk\piper"
$venv = Join-Path $piperRoot "venv"
$voicesDir = Join-Path $piperRoot "voices"
$venvPython = Join-Path $venv "Scripts\python.exe"

Write-Host "Using Python: $python"

if (-not (Test-Path $venvPython)) {
    Write-Host "Creating venv at $venv ..."
    if ($python -eq "py") { & py -3 -m venv $venv } else { & $python -m venv $venv }
}

Write-Host "Piper 1.4.2 is GPL-3.0-or-later and is installed separately from JTalk."
Write-Host "Voice-model licenses vary; review the stored MODEL_CARD files before use."
if (-not $AcceptVoiceLicenses) {
    $answer = Read-Host "Type YES to install Piper and download the requested voices"
    if ($answer -cne "YES") { throw "Piper/voice licenses were not accepted" }
}

Write-Host "Installing piper-tts 1.4.2 ..."
& $venvPython -m pip install --quiet "piper-tts==1.4.2"

New-Item -ItemType Directory -Force $voicesDir | Out-Null
Write-Host "Downloading voices: $($Voices -join ', ') ..."
& $venvPython -m piper.download_voices --data-dir $voicesDir @Voices

foreach ($voice in $Voices) {
    if ($voice -match '^(?<locale>[a-z]{2}_[A-Z]{2})-(?<name>.+)-(?<quality>x_low|low|medium|high)$') {
        $language = $Matches.locale.Substring(0, 2)
        $cardUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/main/$language/$($Matches.locale)/$($Matches.name)/$($Matches.quality)/MODEL_CARD"
        $cardPath = Join-Path $voicesDir "$voice.MODEL_CARD.md"
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $cardUrl -OutFile $cardPath
        }
        catch {
            Write-Warning "Could not download model card for $voice; review $cardUrl manually."
        }
    }
}

$piperExe = Join-Path $venv "Scripts\piper.exe"
if (-not (Test-Path $piperExe)) { throw "piper.exe missing after install ($piperExe)" }

Write-Host "Smoke test ..."
$testWav = Join-Path $env:TEMP "jtalk-piper-test.wav"
& $piperExe -m $Voices[0] --data-dir $voicesDir -f $testWav -- "Piper is installed and working."
if ((Get-Item $testWav).Length -lt 1000) { throw "piper produced no audio" }
Remove-Item $testWav -Force

Write-Host "Piper ready: $piperExe"
Write-Host "Switch jtalk to it with: jtalk engine piper"
