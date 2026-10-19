# AI Usage Widget

A compact, always-on-top Windows widget for Codex remaining usage (5-hour / weekly), reset countdowns, and optional DeepSeek API balance. Glass styling, animated bars, a drag handle, top snapping, and click-through content.

## Quick start

1. Download the **Windows x64 ZIP** from [Releases](https://github.com/huleixin/ai-usage-widget/releases/latest), not the source archive.
2. Extract everything and open `AIUsageWidget.exe`. The release includes the .NET runtime.
3. Complete the first-run settings. Install Codex and sign in with your own ChatGPT account. Optionally enter your own DeepSeek API key.
4. Open settings with ⚙ or `Configure.cmd`. Autostart is optional.

Recommended: Windows 11 x64. Windows 10 2004+ has a software blur fallback; appearance and performance may differ. Other platforms are not packaged.

Settings live under `%LOCALAPPDATA%\AIUsageWidget`. DeepSeek keys are stored in the current user's Windows Credential Manager, never in the settings JSON. Codex authentication remains with the official Codex client. No telemetry, shared accounts, or application backend. DeepSeek requests go only to the official balance endpoint. API-key-only Codex accounts do not expose ChatGPT subscription limits.

Build on Windows with .NET 8+ SDK, Visual Studio 2022 C++ Build Tools, and a Windows SDK:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1
```

MIT licensed. Unofficial community project; not affiliated with OpenAI, DeepSeek, or Apple.
