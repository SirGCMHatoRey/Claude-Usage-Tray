using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ClaudeUsageTray.Infrastructure.ClaudeAi;

public interface IWebViewApiGateway
{
    Task<(int StatusCode, string Body)> GetJsonAsync(string path, CancellationToken ct = default);
}

public sealed class WebViewApiGateway : IWebViewApiGateway, IDisposable
{
    private readonly ILogger<WebViewApiGateway> _logger;
    private WebView2? _webView;
    private Window? _hostWindow;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Dispatcher? _uiDispatcher;

    public WebViewApiGateway(ILogger<WebViewApiGateway> logger)
    {
        _logger = logger;
    }

    public async Task<(int StatusCode, string Body)> GetJsonAsync(string path, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        _logger.LogDebug("WebView fetch: {Path}", path);
        var outerTcs = new TaskCompletionSource<(int, string)>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = _uiDispatcher!.InvokeAsync(async () =>
        {
            try
            {
                // Stay on /chats page; use fetch() Promise chain (avoids navigation/download issues)
                var escapedPath = path.Replace("'", "\\'");
                // Plain .then() chain — ExecuteScriptAsync awaits Promises per WebView2 spec
                var script = "fetch('https://claude.ai" + escapedPath + "',{credentials:'include',headers:{'Accept':'application/json'}})" +
                             ".then(function(r){var s=r.status;return r.text().then(function(b){return JSON.stringify({status:s,body:b});});})" +
                             ".catch(function(e){return JSON.stringify({status:0,body:e.toString()});})";

                var raw = await _webView!.CoreWebView2.ExecuteScriptAsync(script);
                _logger.LogDebug("fetch raw ({Len}): {Preview}", raw.Length, raw.Length > 300 ? raw[..300] : raw);

                // If ExecuteScriptAsync awaited the Promise, raw is a JSON-encoded string
                // If it did NOT await (returned Promise object), raw is "{}"
                if (raw is "{}" or "null" or "undefined")
                {
                    _logger.LogWarning("ExecuteScriptAsync returned Promise object instead of resolved value");
                    outerTcs.TrySetResult((0, "Promise not resolved"));
                    return;
                }

                var jsonStr = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? raw;
                using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                var status = doc.RootElement.GetProperty("status").GetInt32();
                var body = doc.RootElement.GetProperty("body").GetString() ?? "";

                _logger.LogDebug("fetch result: {Status} body: {Preview}", status, body.Length > 300 ? body[..300] : body);
                outerTcs.TrySetResult((status, body));
            }
            catch (Exception ex)
            {
                outerTcs.TrySetException(ex);
            }
        });

        return await outerTcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    _uiDispatcher = System.Windows.Application.Current.Dispatcher;

                    _hostWindow = new Window
                    {
                        Width = 1,
                        Height = 1,
                        WindowStyle = WindowStyle.None,
                        ShowInTaskbar = false,
                        Opacity = 0,
                        AllowsTransparency = true
                    };
                    _webView = new WebView2();
                    _hostWindow.Content = _webView;
                    _hostWindow.Show();

                    await _webView.EnsureCoreWebView2Async();
                    _logger.LogDebug("WebViewApiGateway: CoreWebView2 ready");

                    var navTcs = new TaskCompletionSource<bool>();
                    void NavDone(object? s, CoreWebView2NavigationCompletedEventArgs e)
                    {
                        _webView.CoreWebView2.NavigationCompleted -= NavDone;
                        navTcs.TrySetResult(true);
                    }
                    _webView.CoreWebView2.NavigationCompleted += NavDone;
                    _webView.CoreWebView2.Navigate("https://claude.ai/chats");
                    await navTcs.Task;

                    _logger.LogDebug("WebViewApiGateway: initial navigation complete");

                    // Diagnostic: verify ExecuteScriptAsync awaits Promises
                    var promiseTest = await _webView.CoreWebView2.ExecuteScriptAsync("Promise.resolve(42)");
                    _logger.LogDebug("Promise test: {Result} (expect '42')", promiseTest);
                    var titleTest = await _webView.CoreWebView2.ExecuteScriptAsync("document.title");
                    _logger.LogDebug("Page title: {Title}", titleTest);

                    _initialized = true;
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            await tcs.Task.WaitAsync(ct);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _uiDispatcher?.InvokeAsync(() =>
        {
            _webView?.Dispose();
            _hostWindow?.Close();
        });
        _initLock.Dispose();
    }
}
