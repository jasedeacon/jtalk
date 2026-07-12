# jtalk

Spoken notifications for **Claude Code** and **OpenAI Codex CLI** on Windows.
When an agent finishes a turn or needs your attention, jtalk tells you out loud —
"Claude: fixed the auth bug, all tests pass" — so you don't have to watch terminals.

## How it works

```
Claude Code ── Stop/Notification/SessionEnd hook ──▶ jtalk.exe hook claude ──┐
                                                                             │ named pipe
Codex CLI ──── Stop/PermissionRequest hook ────────▶ jtalk.exe hook codex ──┼──▶ jtalk daemon
                                                                             │    ├─ LLM one-line summary (Anthropic/OpenAI, optional)
jtalk say "..." ─────────────────────────────────────────────────────────────┘    ├─ FIFO speech queue (never talks over itself)
                                                                                  ├─ TTS: windows | piper | openai
                                                                                  └─ tray icon (mute/volume/voice/engine)
```

- One dual-mode exe: `jtalk daemon` is the resident speaker (auto-started on demand,
  mutex-guarded single instance); `jtalk hook <source>` is the fire-and-forget adapter
  the CLI hooks invoke (always exits 0, never writes stdout, never blocks the CLI).
- Turn summaries use an offline markdown-stripping fallback by default. You can explicitly
  enable Claude Haiku or an OpenAI mini model for shorter summaries. Attention/session-end
  events always speak instantly without an LLM call.
- Each tool gets its own voice plus a spoken prefix ("Claude: …" / "Codex: …").

## Install

Requirements: Windows 11 x64, Windows PowerShell 5.1 or PowerShell 7, and Claude Code
and/or Codex CLI ≥ 0.124.

Download and extract `jtalk-v<version>-win-x64.zip` from the GitHub release, then run:

```powershell
.\install\install.ps1              # install bundled exe + wire Claude Code + Codex
.\install\install.ps1 -WithPiper   # also set up the local neural TTS tier
```

The installer merges its handlers into existing Codex hooks and does not replace unrelated
configuration. **Codex trust step (one-time):** start an interactive `codex` session, run
`/hooks`, and trust the JTalk hooks.

To build from source instead, install the .NET 10 SDK, clone this repository, and run the
same script. It detects that no bundled executable exists and publishes the project first.

Uninstall with `.\install\uninstall.ps1` (add `-Purge` to also delete config/logs/piper).

## TTS engines

| Engine | Quality | Needs | Notes |
|---|---|---|---|
| `windows` (default) | basic | nothing | WinRT OneCore voices (George/Hazel/Susan) |
| `piper` | good neural, offline | `install\setup-piper.ps1` (Python 3.9+) | Optional GPL-3.0-or-later program; voice licenses vary |
| `openai` | best, cloud | `OPENAI_API_KEY` | `gpt-4o-mini-tts`; streamed with a bounded download deadline |

Switch live: `jtalk engine piper` or via the tray icon. On failure an engine falls
down the chain (`openai → piper → windows`) so something always speaks.

## LLM summaries

Cloud summaries are **off by default**, even if API-key environment variables already
exist. Enable them explicitly with `jtalk summarizer auto`, `anthropic`, or `openai`;
disable them again with `jtalk summarizer off`. Backend `auto` prefers Anthropic, then
OpenAI. Any missing key, timeout, or provider failure uses the offline fallback.

When enabled, JTalk sends up to `maxInputChars` characters from the coding assistant's
final reply to the selected provider. That text can contain source snippets, paths, or
other sensitive material, and normal provider API billing/data policies apply. JTalk does
not scan project files independently. Prefer environment variables over literal config
keys because config values are stored as plain text in your user profile.

Keys can also come from a **custom-named env var** via `*ApiKeyEnvVar` config fields
(see the config reference) — useful when your key already lives under another name.
Key resolution order (first non-empty wins):

- OpenAI TTS: `openaiTts.apiKey` → env named by `openaiTts.apiKeyEnvVar` →
  `summarizer.openaiApiKey` → env named by `summarizer.openaiApiKeyEnvVar` → `OPENAI_API_KEY`
- Summarizer (per backend): `summarizer.<x>ApiKey` → env named by
  `summarizer.<x>ApiKeyEnvVar` → `ANTHROPIC_API_KEY` / `OPENAI_API_KEY`

Note: the daemon reads env vars from its own process environment — after changing an
env var (or the machine-level value), restart the daemon (`jtalk quit`; it respawns on
the next event). Config fields hot-reload without a restart except `maxQueue`, whose
channel capacity is fixed when the daemon starts.

## CLI

```
jtalk say <text...>          speak text (starts the daemon if needed)
jtalk status                 daemon status
jtalk mute | unmute          toggle speech (persists)
jtalk volume <0-100>         set volume
jtalk engine <name>          windows | piper | openai
jtalk summarizer <backend>   off | auto | anthropic | openai
jtalk voice list             list voices per engine (alias: jtalk voices)
jtalk voice <tool> <name>    set claude/codex voice for the current engine
jtalk quit                   stop the daemon
jtalk daemon                 run the daemon in the foreground
jtalk hook <source>          hook adapter mode (called by CLI hooks, reads stdin)
jtalk version                print version
```

Config lives at `%APPDATA%\jtalk\config.json` and hot-reloads on save. Values are
normalized on load: engine names are case-insensitive, and numeric fields are clamped
to their valid ranges (volume 0–100, rate 0.25–6.0, etc.) so a hand-edited file can't
put the daemon in a broken state. Logs are in
`%APPDATA%\jtalk\logs\`. Set `logPayloads: true` to log mapped event text, summary
results, and the exact text handed to TTS; `logLevel: "debug"` includes mapped payload
text and timing diagnostics. The untouched raw hook JSON is not logged. Payload logging
is off by default because assistant replies may be sensitive.

## Config reference (defaults)

```jsonc
{
  "engine": "windows",
  "muted": false, "volume": 80, "rate": 1.0,
  "prefixEnabled": true,          // speak "Claude:" / "Codex:" prefix
  "speakProject": false,          // add ", in <folder>" to the prefix
  "tools": {
    "claude": { "prefix": "Claude", "windowsVoice": "Hazel", "piperVoice": "en_GB-alba-medium", "openaiVoice": "nova" },
    "codex":  { "prefix": "Codex",  "windowsVoice": "George", "piperVoice": "en_GB-alan-medium", "openaiVoice": "onyx" }
  },
  "summarizer": {
    "backend": "off",             // off | auto | anthropic | openai
    "anthropicModel": "claude-haiku-4-5",
    "openaiModel": "gpt-5-mini",
    "timeoutMs": 5000, "maxInputChars": 2000,
    "anthropicApiKey": null,      // literal key (config wins over env)
    "openaiApiKey": null,
    "anthropicApiKeyEnvVar": null, // read key from a custom-named env var
    "openaiApiKeyEnvVar": null
  },
  "openaiTts": {
    "model": "gpt-4o-mini-tts",
    "instructions": "Speak at maximum pace while keeping every word crisp and intelligible. Brief, efficient, energetic status update; no filler, no dramatic pauses.",
                                  // pace/tone steering; sent to gpt-* TTS models only
    "apiKey": null,               // literal key
    "apiKeyEnvVar": null          // custom env var name, e.g. "MY_OPENAI_KEY"
  },
  "piper": { "exe": "%APPDATA%\\jtalk\\piper\\venv\\Scripts\\piper.exe", "voicesDir": "%APPDATA%\\jtalk\\piper\\voices" },
  "events": { "turnComplete": true, "attention": true, "sessionEnd": true },
  "maxQueue": 20,                 // oldest cancelled/dropped beyond this; restart after changing
  "idleExitMinutes": 0            // 0 = daemon stays resident
}
```

## Integration details

**Claude Code** — installed as a plugin (`plugin/` in this repo, registered as a local
marketplace). Hooks: `Stop` (turn summary), `Notification` matcher
`permission_prompt|idle_prompt` (attention), `SessionEnd`. All exec-form with a 10 s
timeout, invoking `jtalk.exe hook claude` which reads the hook JSON from stdin.

**Codex CLI** — `%USERPROFILE%\.codex\hooks.json` with `Stop` and `PermissionRequest`
hooks invoking `jtalk.exe hook codex`. Codex has no SessionEnd event, so session-end
announcements are Claude-Code-only. A `PermissionRequest` hook that exits 0 with no
output leaves the approval flow untouched (per Codex hook semantics). For Codex
versions older than the hooks system (< 0.124), use the legacy notify fallback in
user `config.toml` instead (root level, before any `[section]`):

```toml
notify = ["C:\\Users\\<you>\\AppData\\Local\\jtalk\\bin\\jtalk.exe", "hook", "codex-notify"]
```

## Development

```powershell
dotnet restore jtalk.slnx --locked-mode
dotnet build jtalk.slnx -c Release --no-restore
dotnet test jtalk.slnx -c Release --no-build
.\tests\install\Installer.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\install\Installer.Tests.ps1
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution and verification requirements,
[SECURITY.md](SECURITY.md) for private vulnerability reporting, and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for bundled and optional dependencies.

## Tips

- `JTALK_MUTE=1` in a process's environment makes its hooks silent — handy for
  scripted `claude -p` / `codex exec` runs.
- Per-event toggles: set `events.attention` or `events.sessionEnd` to `false` if
  turn summaries are all you want.
- Two agents finishing at once queue politely; distinct voices + prefixes tell
  you who's who.
