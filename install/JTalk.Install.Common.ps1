Set-StrictMode -Version 2.0

function Write-Utf8NoBomAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $directory = [IO.Path]::GetDirectoryName($fullPath)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temp = Join-Path $directory (".{0}.{1}.tmp" -f [IO.Path]::GetFileName($fullPath), [Guid]::NewGuid().ToString("N"))
    $replaceBackup = "$temp.bak"
    $encoding = New-Object System.Text.UTF8Encoding($false)
    try {
        [IO.File]::WriteAllText($temp, $Content, $encoding)
        if ([IO.File]::Exists($fullPath)) {
            [IO.File]::Replace($temp, $fullPath, $replaceBackup)
        }
        else {
            [IO.File]::Move($temp, $fullPath)
        }
    }
    finally {
        if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) }
        if ([IO.File]::Exists($replaceBackup)) { [IO.File]::Delete($replaceBackup) }
    }
}

function Test-JTalkCodexHandler {
    param([object]$Handler)

    if ($null -eq $Handler) { return $false }
    $command = ""
    foreach ($name in @("command", "commandWindows")) {
        $property = $Handler.PSObject.Properties[$name]
        if ($null -ne $property) { $command += " " + [string]$property.Value }
    }
    return $command -match '(?i)jtalk\.exe["'']?\s+hook\s+codex(?:\s|$)'
}

function Remove-JTalkHandlersFromEvent {
    param(
        [Parameter(Mandatory = $true)][object]$Hooks,
        [Parameter(Mandatory = $true)][string]$EventName
    )

    $eventProperty = $Hooks.PSObject.Properties[$EventName]
    if ($null -eq $eventProperty) { return }

    $keptGroups = @()
    foreach ($group in @($eventProperty.Value)) {
        $handlersProperty = $group.PSObject.Properties["hooks"]
        if ($null -eq $handlersProperty) {
            $keptGroups += $group
            continue
        }
        $handlers = @($handlersProperty.Value | Where-Object { -not (Test-JTalkCodexHandler $_) })
        if ($handlers.Count -gt 0) {
            $handlersProperty.Value = $handlers
            $keptGroups += $group
        }
    }
    $eventProperty.Value = $keptGroups
}

function Get-HooksObject {
    param([object]$Document)

    $property = $Document.PSObject.Properties["hooks"]
    if ($null -eq $property) {
        $Document | Add-Member -MemberType NoteProperty -Name hooks -Value ([PSCustomObject]@{})
        return $Document.hooks
    }
    if ($null -eq $property.Value) {
        $property.Value = [PSCustomObject]@{}
    }
    return $property.Value
}

function Merge-JTalkCodexHooks {
    param(
        [Parameter(Mandatory = $true)][string]$HooksPath,
        [Parameter(Mandatory = $true)][string]$TemplatePath,
        [Parameter(Mandatory = $true)][string]$ExePath
    )

    if (Test-Path -LiteralPath $HooksPath) {
        $backup = "$HooksPath.jtalk.bak"
        if (-not (Test-Path -LiteralPath $backup)) {
            Copy-Item -LiteralPath $HooksPath -Destination $backup
        }
        $document = Get-Content -LiteralPath $HooksPath -Raw | ConvertFrom-Json
    }
    else {
        $document = [PSCustomObject]@{ hooks = [PSCustomObject]@{} }
    }
    $hooks = Get-HooksObject $document

    $escapedExe = $ExePath.Replace('\', '\\').Replace('"', '\"')
    $templateText = (Get-Content -LiteralPath $TemplatePath -Raw).Replace('__JTALK_EXE__', $escapedExe)
    $desiredHooks = (Get-HooksObject ($templateText | ConvertFrom-Json))

    foreach ($eventName in @("Stop", "PermissionRequest")) {
        Remove-JTalkHandlersFromEvent -Hooks $hooks -EventName $eventName
        $existingProperty = $hooks.PSObject.Properties[$eventName]
        $existing = if ($null -eq $existingProperty) { @() } else { @($existingProperty.Value) }
        $desiredProperty = $desiredHooks.PSObject.Properties[$eventName]
        $merged = @($existing) + @($desiredProperty.Value)
        if ($null -eq $existingProperty) {
            $hooks | Add-Member -MemberType NoteProperty -Name $eventName -Value $merged
        }
        else {
            $existingProperty.Value = $merged
        }
    }

    Write-Utf8NoBomAtomic -Path $HooksPath -Content (($document | ConvertTo-Json -Depth 32) + [Environment]::NewLine)
}

function Remove-JTalkCodexHooks {
    param([Parameter(Mandatory = $true)][string]$HooksPath)

    if (-not (Test-Path -LiteralPath $HooksPath)) { return }
    $document = Get-Content -LiteralPath $HooksPath -Raw | ConvertFrom-Json
    $hooks = Get-HooksObject $document
    foreach ($eventName in @("Stop", "PermissionRequest")) {
        Remove-JTalkHandlersFromEvent -Hooks $hooks -EventName $eventName
    }
    Write-Utf8NoBomAtomic -Path $HooksPath -Content (($document | ConvertTo-Json -Depth 32) + [Environment]::NewLine)
}

function Install-FileAtomically {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Destination))
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $temp = "$Destination.new"
    Copy-Item -LiteralPath $Source -Destination $temp -Force
    if ([IO.File]::Exists($Destination)) {
        $backup = "$Destination.replace-bak"
        try {
            [IO.File]::Replace($temp, $Destination, $backup)
        }
        finally {
            if ([IO.File]::Exists($backup)) { [IO.File]::Delete($backup) }
        }
    }
    else {
        [IO.File]::Move($temp, $Destination)
    }
}
