<#
.SYNOPSIS
Installs jtalk without replacing existing Claude or Codex configuration.

.DESCRIPTION
Runs in Windows PowerShell 5.1 and PowerShell 7. In a release archive the bundled
root-level jtalk.exe is installed directly; in a source checkout the project is
published to a staging directory first.
#>
param(
    [switch]$SkipPublish,
    [switch]$SkipPath,
    [switch]$SkipClaude,
    [switch]$SkipCodex,
    [switch]$WithPiper,
    [switch]$AcceptVoiceLicenses,
    [switch]$AutoStart,
    [switch]$DevMode,
    [switch]$SkipSmokeTest,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "jtalk"),
    [string]$ConfigRoot = (Join-Path $env:APPDATA "jtalk"),
    [string]$CodexHome = (Join-Path $env:USERPROFILE ".codex")
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "JTalk.Install.Common.ps1")

$binDir = Join-Path $InstallRoot "bin"
$exe = Join-Path $binDir "jtalk.exe"
$integrationRoot = Join-Path $InstallRoot "integrations\claude"
$stageDir = Join-Path $env:TEMP ("jtalk-publish-{0}-{1}" -f $PID, [Guid]::NewGuid().ToString("N"))

try {
    if (-not $SkipPublish) {
        Write-Host "Installing jtalk.exe to $binDir ..."
        if (Test-Path -LiteralPath $exe) {
            & $exe quit 2>$null | Out-Null
            Start-Sleep -Milliseconds 500
        }
        Get-Process jtalk -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($binDir, [StringComparison]::OrdinalIgnoreCase) } |
            Stop-Process -Force -Confirm:$false -ErrorAction SilentlyContinue

        $bundledExe = Join-Path $repoRoot "jtalk.exe"
        if (Test-Path -LiteralPath $bundledExe) {
            $sourceExe = $bundledExe
        }
        else {
            New-Item -ItemType Directory -Force $stageDir | Out-Null
            dotnet publish (Join-Path $repoRoot "src\JTalk\JTalk.csproj") -c Release -o $stageDir --nologo -v quiet -p:RestoreLockedMode=true
            $sourceExe = Join-Path $stageDir "jtalk.exe"
        }
        if (-not (Test-Path -LiteralPath $sourceExe)) { throw "publish/package did not provide jtalk.exe" }
        Install-FileAtomically -Source $sourceExe -Destination $exe
    }
    elseif (-not (Test-Path -LiteralPath $exe)) {
        throw "-SkipPublish requires an existing $exe"
    }

    if (-not $SkipPath) {
        $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
        $parts = @($userPath -split ';' | Where-Object { $_ })
        if ($parts -notcontains $binDir) {
            [Environment]::SetEnvironmentVariable("Path", (($parts + $binDir) -join ';'), "User")
            Write-Host "Added $binDir to user PATH (new terminals only)."
        }
    }

    New-Item -ItemType Directory -Force (Join-Path $ConfigRoot "logs") | Out-Null

    if (-not $SkipClaude) {
        if (Test-Path -LiteralPath $integrationRoot) {
            Remove-Item -LiteralPath $integrationRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Force $integrationRoot | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot ".claude-plugin") -Destination $integrationRoot -Recurse
        Copy-Item -LiteralPath (Join-Path $repoRoot "plugin") -Destination $integrationRoot -Recurse
        $installedTemplate = Join-Path $integrationRoot "plugin\hooks\hooks.template.json"
        $installedHooks = Join-Path $integrationRoot "plugin\hooks\hooks.json"
        $hooksJson = (Get-Content -LiteralPath $installedTemplate -Raw).Replace('__JTALK_EXE__', $exe.Replace('\', '\\'))
        Write-Utf8NoBomAtomic -Path $installedHooks -Content $hooksJson

        if ($DevMode) {
            Write-Host "DevMode: start Claude Code with: claude --plugin-dir $integrationRoot\plugin"
        }
        else {
            Write-Host "Registering Claude Code plugin ..."
            claude plugin marketplace add $integrationRoot 2>&1 | Out-Null
            claude plugin install jtalk@jtalk 2>&1 | Out-Null
            Write-Host "Claude Code plugin installed."
        }
    }

    if (-not $SkipCodex) {
        New-Item -ItemType Directory -Force $CodexHome | Out-Null
        $codexHooks = Join-Path $CodexHome "hooks.json"
        Merge-JTalkCodexHooks -HooksPath $codexHooks -TemplatePath (Join-Path $repoRoot "codex\hooks.template.json") -ExePath $exe
        Write-Host "JTalk Codex hooks merged. Open /hooks once to review and trust them."
    }

    if ($WithPiper) {
        $piperArgs = @{}
        if ($AcceptVoiceLicenses) { $piperArgs.AcceptVoiceLicenses = $true }
        & (Join-Path $PSScriptRoot "setup-piper.ps1") @piperArgs
    }

    if ($AutoStart) {
        Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "jtalk" -Value "`"$exe`" daemon"
        Write-Host "Registered jtalk daemon to start at login."
    }

    if (-not $SkipSmokeTest) {
        Write-Host "Smoke test ..."
        & $exe say "jtalk is installed and speaking."
    }
    Write-Host "Done. Cloud summaries are off by default. Config: $ConfigRoot\config.json"
}
finally {
    if (Test-Path -LiteralPath $stageDir) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
