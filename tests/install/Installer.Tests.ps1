$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repoRoot "install\JTalk.Install.Common.ps1")

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

foreach ($script in Get-ChildItem (Join-Path $repoRoot "install") -Filter *.ps1) {
    $tokens = $null
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    Assert-True (@($parseErrors).Count -eq 0) "$($script.Name) must parse in this PowerShell version"
}

$temp = Join-Path $env:TEMP ("jtalk-installer-tests-{0}" -f [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $temp | Out-Null
try {
    $hooksPath = Join-Path $temp "hooks.json"
    $original = @'
{
  "custom": { "keep": true },
  "hooks": {
    "PreToolUse": [{ "matcher": "Bash", "hooks": [{ "type": "command", "command": "policy.exe" }] }],
    "Stop": [{ "hooks": [{ "type": "command", "command": "other-notifier.exe" }] }]
  }
}
'@
    Write-Utf8NoBomAtomic -Path $hooksPath -Content $original
    $template = Join-Path $repoRoot "codex\hooks.template.json"
    $exe = "C:\Users\Example User\AppData\Local\jtalk\bin\jtalk.exe"

    Merge-JTalkCodexHooks -HooksPath $hooksPath -TemplatePath $template -ExePath $exe
    Merge-JTalkCodexHooks -HooksPath $hooksPath -TemplatePath $template -ExePath $exe
    $merged = Get-Content -LiteralPath $hooksPath -Raw | ConvertFrom-Json
    $jtalkStop = @($merged.hooks.Stop | ForEach-Object { $_.hooks } | Where-Object { Test-JTalkCodexHandler $_ })
    $jtalkPermission = @($merged.hooks.PermissionRequest | ForEach-Object { $_.hooks } | Where-Object { Test-JTalkCodexHandler $_ })
    Assert-True ($jtalkStop.Count -eq 1) "reinstall must leave exactly one Stop handler"
    Assert-True ($jtalkPermission.Count -eq 1) "reinstall must leave exactly one PermissionRequest handler"
    Assert-True (@($merged.hooks.PreToolUse).Count -eq 1) "unrelated events must survive"
    Assert-True ($merged.custom.keep -eq $true) "unknown top-level properties must survive"
    Assert-True ((Get-Content -LiteralPath "$hooksPath.jtalk.bak" -Raw) -eq $original) "first backup must not be overwritten"

    $bytes = [IO.File]::ReadAllBytes($hooksPath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    Assert-True (-not $hasBom) "hooks JSON must be UTF-8 without BOM"

    Remove-JTalkCodexHooks -HooksPath $hooksPath
    $removed = Get-Content -LiteralPath $hooksPath -Raw | ConvertFrom-Json
    Assert-True (@($removed.hooks.Stop).Count -eq 1) "unrelated Stop group must survive uninstall"
    Assert-True (@($removed.hooks.PermissionRequest).Count -eq 0) "JTalk-only event must be empty after uninstall"
    Assert-True ($removed.hooks.Stop[0].hooks[0].command -eq "other-notifier.exe") "unrelated handler must survive uninstall"

    Write-Host "Installer helper tests passed."
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
