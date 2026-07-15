# Claude Usage Tray

Windows tray app that shows your claude.ai usage (messages / limits) in a floating widget and system tray icon, refreshed every 2 minutes.

## Why

claude.ai has no public usage API for regular accounts, and the site sits behind Cloudflare. This app logs in through a real embedded browser (WebView2) so it gets a legitimate browser session and `cf_clearance` cookie, then reuses that session to poll the internal usage endpoints — something a plain `HttpClient` can't do, since .NET's TLS fingerprint gets blocked by Cloudflare even with valid cookies.

## Features

- System tray icon + small floating widget showing current usage
- Background sync every 2 minutes
- Login once via embedded browser window; session cached in Windows Credential Manager
- Local cache so the widget still shows last-known state if a sync fails

## Build & Run

Requires Windows (`net8.0-windows` target) and the .NET 8 SDK.

```powershell
# Build (Release)
dotnet build src/ClaudeUsageTray/ClaudeUsageTray.csproj -c Release

# Run
Start-Process src/ClaudeUsageTray/bin/Release/net8.0-windows/ClaudeUsageTray.exe
```

On first run, no session is stored, so a settings window opens for login before the tray icon and widget appear.

Logs: `%LOCALAPPDATA%\ClaudeUsageTray\logs\app-YYYYMMDD.log` (daily rolling, Serilog).

## Architecture

Single WPF project (`src/ClaudeUsageTray/`), four layers:

```
Domain/          pure models, no dependencies
Application/     state store + sync orchestrator (background service)
Infrastructure/  API access, credential storage, cache, logging
Presentation/    WPF windows, ViewModel, tray icon
```

Data flow:

```
WebViewApiGateway (hidden WebView2 browser)
  -> ClaudeAiUsageProvider (HTTP client logic)
    -> SyncOrchestrator (BackgroundService, polls every 2 min)
      -> UsageStore (BehaviorSubject<AppState>)
        -> FloatingWidgetViewModel -> FloatingWidget (WPF window)
        -> TrayController (NotifyIcon)
```

Session cookies (`sessionKey`, `cf_clearance`) and user agent are captured once during login (`ClaudeLoginWindow`, a real WebView2 browser) and stored in Windows Credential Manager. The hidden `WebViewApiGateway` shares the same WebView2 profile, so it reuses that session for every poll.

See `CLAUDE.md` for full implementation notes (namespace collision workarounds, WebView2 Promise-handling quirks, credential storage details).

## Project layout

```
src/ClaudeUsageTray/
  Domain/Models/            AppState, UsageMetrics, VisualState
  Application/State/        UsageStore
  Application/Sync/         SyncOrchestrator
  Infrastructure/Api/       AnthropicAdminApiClient
  Infrastructure/ClaudeAi/  WebViewApiGateway, ClaudeAiUsageProvider
  Infrastructure/Cache/     UsageCache
  Infrastructure/Security/  CredentialStore (Windows Credential Manager)
  Infrastructure/Logging/   Serilog setup
  Presentation/Login/       ClaudeLoginWindow
  Presentation/Settings/    SettingsWindow
  Presentation/Tray/        TrayController
  Presentation/Widget/      FloatingWidget, FloatingWidgetViewModel
  Presentation/Converters/  WPF value converters
```

No test project. No lint step.
