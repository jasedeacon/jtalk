<# .SYNOPSIS Uninstalls jtalk while preserving unrelated user configuration. #>
param(
    [switch]$Purge,
    [switch]$SkipPath,
    [switch]$SkipClaude,
    [switch]$SkipCodex,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "jtalk"),
    [string]$ConfigRoot = (Join-Path $env:APPDATA "jtalk"),
    [string]$CodexHome = (Join-Path $env:USERPROFILE ".codex")
)

$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "JTalk.Install.Common.ps1")
$binDir = Join-Path $InstallRoot "bin"
$exe = Join-Path $binDir "jtalk.exe"

if (Test-Path -LiteralPath $exe) {
    & $exe quit 2>$null | Out-Null
    Start-Sleep -Milliseconds 500
}
Get-Process jtalk -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($binDir, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force -Confirm:$false -ErrorAction SilentlyContinue

if (-not $SkipClaude) {
    claude plugin uninstall jtalk@jtalk 2>&1 | Out-Null
    claude plugin marketplace remove jtalk 2>&1 | Out-Null
    Remove-Item -LiteralPath (Join-Path $InstallRoot "integrations\claude") -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Claude Code plugin removed."
}

if (-not $SkipCodex) {
    $codexHooks = Join-Path $CodexHome "hooks.json"
    Remove-JTalkCodexHooks -HooksPath $codexHooks
    Write-Host "JTalk Codex hooks removed; unrelated hooks preserved."
}

if (-not $SkipPath) {
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $newPath = @($userPath -split ';' | Where-Object { $_ -and $_ -ne $binDir }) -join ';'
    if ($newPath -ne $userPath) {
        [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
        Write-Host "Removed $binDir from user PATH."
    }
}

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "jtalk" -ErrorAction SilentlyContinue

if ($Purge) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $ConfigRoot -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Purged installed files, config, logs, and piper environment."
}

Write-Host "jtalk uninstalled."
