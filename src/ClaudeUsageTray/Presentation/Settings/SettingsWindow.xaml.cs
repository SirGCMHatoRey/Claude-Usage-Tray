using System.Windows;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using ClaudeUsageTray.Application.Sync;
using ClaudeUsageTray.Infrastructure.Security;
using ClaudeUsageTray.Presentation.Login;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTray.Presentation.Settings;

public partial class SettingsWindow : Window
{
    private readonly ICredentialStore _credentials;
    private readonly SyncOrchestrator _sync;
    private readonly ILogger<SettingsWindow> _logger;

    public SettingsWindow(
        ICredentialStore credentials,
        SyncOrchestrator sync,
        ILogger<SettingsWindow> logger)
    {
        InitializeComponent();
        _credentials = credentials;
        _sync = sync;
        _logger = logger;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_credentials.HasSessionKey())
        {
            StatusDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0x4C, 0xC9, 0x74));
            StatusText.Text = "Signed in to Claude.ai";
            SignInBtn.Content = "Re-authenticate";
            SignOutBtn.Visibility = Visibility.Visible;
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0x88, 0x88, 0x88));
            StatusText.Text = "Not signed in";
            SignInBtn.Content = "Sign in to Claude.ai";
            SignOutBtn.Visibility = Visibility.Collapsed;
        }
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void OnSignIn(object sender, RoutedEventArgs e)
    {
        var loginWindow = new ClaudeLoginWindow
        {
            Owner = this
        };

        var result = loginWindow.ShowDialog();

        if (result == true && !string.IsNullOrWhiteSpace(loginWindow.ExtractedSessionKey))
        {
            try
            {
                _credentials.SaveSessionKey(loginWindow.ExtractedSessionKey!);
                if (!string.IsNullOrWhiteSpace(loginWindow.ExtractedCfClearance))
                    _credentials.SaveCfClearance(loginWindow.ExtractedCfClearance!);
                if (!string.IsNullOrWhiteSpace(loginWindow.DetectedUserAgent))
                    _credentials.SaveUserAgent(loginWindow.DetectedUserAgent!);
                RefreshStatus();
                _ = Task.Run(async () =>
                {
                    try { await _sync.ForceSyncAsync(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Post-login sync failed"); }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save session key");
                ErrorText.Text = $"Failed to save session: {ex.Message}";
                ErrorText.Visibility = Visibility.Visible;
            }
        }
        else if (result == false)
        {
            // User cancelled — no-op
        }
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        _credentials.DeleteSessionKey();
        RefreshStatus();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
