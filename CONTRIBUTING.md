# Contributing to JTalk

Thanks for helping improve JTalk. It is a Windows 11 x64 application targeting .NET 10.

## Development setup

Install the .NET 10 SDK and use Windows PowerShell 5.1 or PowerShell 7. Restore, build,
and test from the repository root:

```powershell
dotnet restore jtalk.slnx --locked-mode
dotnet build jtalk.slnx -c Release --no-restore
dotnet test jtalk.slnx -c Release --no-build
.\tests\install\Installer.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\install\Installer.Tests.ps1
```

Warnings and configured code-style diagnostics are errors. Update and commit package lock
files when intentionally changing dependencies.

## Pull requests

- Keep changes focused and add regression tests for behavior changes.
- Do not put API keys, hook payloads, user paths, logs, or generated machine-local files in commits.
- Preserve unrelated Codex hooks and user configuration in installer changes.
- Do not make tests depend on a real API key, network, speaker, or the user's live config.
- Describe manual audio verification when changing WinRT, Piper, OpenAI TTS, or NAudio behavior.

By participating, you agree to follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
