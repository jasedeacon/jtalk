# Security Policy

## Supported versions

Until JTalk reaches 1.0, only the most recent GitHub release receives security fixes.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting for this repository. Do not open a public
issue containing API keys, assistant replies, hook payloads, local paths, or exploit details.
Include the affected version, reproduction steps, impact, and any suggested remediation.

## Data and trust boundaries

- Cloud summaries are off by default. When explicitly enabled, mapped assistant reply text
  is sent to the selected Anthropic or OpenAI API using the user's credentials.
- OpenAI TTS sends spoken text to OpenAI only after the user selects the OpenAI engine.
- Literal config keys and optional payload logs are stored in the user's profile as plain text.
- The named pipe and hook commands are local per-user integration surfaces, not remote APIs.
- Piper and voice models are optional third-party downloads with separate licenses.
