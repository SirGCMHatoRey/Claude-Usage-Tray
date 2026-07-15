using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace ClaudeUsageTray.Presentation.Login;

public partial class ClaudeLoginWindow : Window
{
    public string? ExtractedSessionKey { get; private set; }   // raw value
    public string? ExtractedCfClearance { get; private set; } // raw value
    public string? DetectedUserAgent { get; private set; }

    private bool _extracting;

    public ClaudeLoginWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Browser.EnsureCoreWebView2Async();
        Browser.CoreWebView2.Navigate("https://claude.ai");
    }

    private async void OnNavigationCompleted(object sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_extracting) return;

        var url = Browser.Source?.ToString() ?? "";
        if (url.Contains("claude.ai") && !url.Contains("/login") && !url.Contains("/auth"))
            await TryExtractSessionAsync();
        else
            StatusLabel.Text = "Waiting for sign-in...";
    }

    private async Task TryExtractSessionAsync()
    {
        _extracting = true;
        StatusLabel.Text = "Signed in — extracting session...";
        await Task.Delay(800);

        var cookies = await Browser.CoreWebView2.CookieManager
            .GetCookiesAsync("https://claude.ai");

        var sessionCookie = cookies.FirstOrDefault(c =>
            c.Name.Equals("sessionKey", StringComparison.OrdinalIgnoreCase));

        if (sessionCookie is null)
        {
            var names = string.Join(", ", cookies.Select(c => c.Name));
            StatusLabel.Text = $"sessionKey not found. Cookies: [{names}]";
            _extracting = false;
            return;
        }

        var cfCookie = cookies.FirstOrDefault(c =>
            c.Name.Equals("cf_clearance", StringComparison.OrdinalIgnoreCase));

        ExtractedSessionKey = sessionCookie.Value;
        ExtractedCfClearance = cfCookie?.Value;

        DetectedUserAgent = await Browser.CoreWebView2.ExecuteScriptAsync("navigator.userAgent");
        DetectedUserAgent = DetectedUserAgent?.Trim('"');

        var captured = cfCookie is not null
            ? "sessionKey + cf_clearance captured."
            : "sessionKey captured (no cf_clearance found).";
        StatusLabel.Text = $"{captured} Closing...";
        await Task.Delay(500);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
