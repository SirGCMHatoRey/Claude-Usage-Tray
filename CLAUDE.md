# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```powershell
# Build (Release)
dotnet build src/ClaudeUsageTray/ClaudeUsageTray.csproj -c Release

# Run
Start-Process src/ClaudeUsageTray/bin/Release/net8.0-windows/ClaudeUsageTray.exe

# Kill before rebuild (exe is locked while running)
Stop-Process -Name "ClaudeUsageTray" -Force -ErrorAction SilentlyContinue

# Logs (tail)
Get-Content "$env:LOCALAPPDATA\ClaudeUsageTray\logs\app-$(Get-Date -f yyyyMMdd).log" -Tail 50
```

No test project exists. No lint step. Target is `net8.0-windows` only — build requires Windows.

## Architecture

**Single WPF project** (`src/ClaudeUsageTray/`) with four layers:

```
Domain/          → pure models, no dependencies
Application/     → state store + sync orchestrator (background service)
Infrastructure/  → API access, credential storage, cache, logging
Presentation/    → WPF windows, ViewModel, tray icon
```

### Data flow

```
WebViewApiGateway (WebView2 browser)
  → ClaudeAiUsageProvider (HTTP client logic)
    → SyncOrchestrator (BackgroundService, polls every 2 min)
      → UsageStore (BehaviorSubject<AppState>)
        → FloatingWidgetViewModel (INPC, subscribes to StateStream)
          → FloatingWidget (WPF window)
        → TrayController (NotifyIcon, subscribes to StateStream)
```

### Key design decisions

**Why WebViewApiGateway instead of HttpClient?** claude.ai is behind Cloudflare. .NET's TLS fingerprint (JA3) differs from Chromium, so `cf_clearance` cookies are rejected even when present. The gateway hosts a hidden 1×1 WPF `Window` with a `WebView2` control, navigates it to `https://claude.ai/chats` on init (establishes browser session + CF cookies), then calls `ExecuteScriptAsync` with a `fetch().then()` Promise chain for each API call. This runs entirely in the browser's TLS context.

**ExecuteScriptAsync and Promises**: WebView2 spec says it awaits Promises. Use plain `.then()` chains, NOT `async function` IIFEs — the IIFE pattern returned `{}` (serialized Promise object) in testing on this machine. Result is a JSON-encoded string; `Deserialize<string>(raw)` unwraps the outer quotes, then parse the inner JSON.

**Namespace collisions** (WPF + this project's namespaces conflict):
- `VisualState` → alias: `using VisualState = ClaudeUsageTray.Domain.Models.VisualState;`
- `System.Windows.Application` vs `ClaudeUsageTray.Application` namespace → use fully qualified `System.Windows.Application`
- `System.Windows.Media.Color` vs `System.Drawing.Color` → alias: `using WpfColor = System.Windows.Media.Color;`

**Credential storage**: Windows Credential Manager via P/Invoke (`advapi32.dll`). Three separate entries: session key value, cf_clearance value, user agent. CRED_MAX_CREDENTIAL_BLOB_SIZE ≈ 2560 bytes — store only raw cookie VALUES, never full `name=value` cookie strings.

**State management**: `UsageStore` wraps `BehaviorSubject<AppState>`. All state mutations go through `AppStateUpdate.Apply()` (discriminated union: `MetricsUpdated`, `SyncFailed`, `SyncStarted`). `FloatingWidgetViewModel` subscribes and marshals to UI thread manually via `Dispatcher.Invoke`.

**App startup**: `ShutdownMode="OnExplicitShutdown"` (no main window). On first run (no stored session key), `SettingsWindow` is shown as a dialog before the tray and widget appear.

**Login flow**: `ClaudeLoginWindow` embeds a real WebView2 browser. After the user signs in to claude.ai, the window extracts `sessionKey` and `cf_clearance` cookie values, plus `navigator.userAgent`. These are stored separately in Credential Manager. The hidden `WebViewApiGateway` WebView2 instance shares the same WebView2 user data folder (default profile), so cookies from login are immediately available to it.

### claude.ai API

Endpoint sequence: `GET /api/organizations` → get org UUID → try `/api/organizations/{id}/usage`, `/limits`, `/entitlements`, `/{id}` in order. `TryParseUsageMetrics` uses flexible field name lookup (`message_count`, `messages_used`, `weekly_messages_used`, etc.) because the actual field names are undocumented and may change.

### Logging

Serilog, daily rolling file: `%LOCALAPPDATA%\ClaudeUsageTray\logs\app-YYYYMMDD.log`. Debug level enabled. All sync attempts, state transitions, and WebView2 fetch raw responses are logged at Debug.
