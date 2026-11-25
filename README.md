# QuotaRing

A lightweight native Windows 11 desktop floating widget built with .NET 9 WPF that displays real-time remaining usage quotas for **Antigravity**, **OpenAI Codex**, and **Claude Code** as circular ring gauges.

## Features

- **Circular Remaining-Quota Rings**: Colored arc shows remaining quota, gray track shows used quota, with remaining percentage centered and reset countdown below.
- **Automatic Threshold Colors**: Emerald (`> 35%`), Amber (`≤ 35%`), Red (`≤ 15%`).
- **Multi-Provider Support (Local-First)**:
  - **Antigravity**: Queries the official `agy -p /usage --output-format json` CLI (Gemini 5h/7d & Claude/GPT 5h/7d).
  - **Codex**: Reads local `~/.codex/auth.json` and queries the ChatGPT backend usage endpoint (5h & 7d).
  - **Claude Code**: Reads local `~/.claude/.credentials.json` (`claudeAiOauth`) and queries `api.anthropic.com/api/oauth/usage` (5h & 7d).
- **Clean Desktop UX**: Frameless rounded card, light/dark themes, always-on-top toggle, drag & remember position, lock position, system tray icon, and optional start-at-login.
- **Privacy First**: All credentials stay on your local machine and are only sent to their respective official provider endpoints.

## Requirements

- Windows 10 / 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (to build from source)

## Build & Run

```powershell
# Build Release
dotnet build QuotaRing\QuotaRing.csproj -c Release

# Run unit tests
dotnet run --project QuotaRing.Tests\QuotaRing.Tests.csproj -c Release

# Launch widget
.\QuotaRing\bin\Release\net9.0-windows\QuotaRing.exe
```

## License

MIT
