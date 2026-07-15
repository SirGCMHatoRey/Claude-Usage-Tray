using System.Drawing;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Forms;
using ClaudeUsageTray.Application.State;
using ClaudeUsageTray.Application.Sync;
using ClaudeUsageTray.Domain.Models;
using VisualState = ClaudeUsageTray.Domain.Models.VisualState;
using ClaudeUsageTray.Presentation.Settings;
using ClaudeUsageTray.Presentation.Widget;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTray.Presentation.Tray;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly IUsageStore _store;
    private readonly SyncOrchestrator _sync;
    private readonly FloatingWidget _widget;
    private readonly SettingsWindow _settingsWindow;
    private readonly ILogger<TrayController> _logger;
    private readonly IDisposable _subscription;

    private bool _widgetVisible = true;

    public TrayController(
        IUsageStore store,
        SyncOrchestrator sync,
        FloatingWidget widget,
        SettingsWindow settingsWindow,
        ILogger<TrayController> logger)
    {
        _store = store;
        _sync = sync;
        _widget = widget;
        _settingsWindow = settingsWindow;
        _logger = logger;

        _notifyIcon = new NotifyIcon
        {
            Text = "Claude Usage",
            Icon = LoadIcon(VisualState.Loading),
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.MouseDoubleClick += (_, _) => ToggleWidget();

        _subscription = store.StateStream
            .DistinctUntilChanged(s => s.VisualState)
            .Subscribe(OnStateChanged);
    }

    private void OnStateChanged(AppState state)
    {
        var icon = LoadIcon(state.VisualState);
        var tooltip = BuildTooltip(state);

        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            _notifyIcon.Icon = icon;
            _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
        }
        else
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _notifyIcon.Icon = icon;
                _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
            });
        }
    }

    private static string BuildTooltip(AppState state) => state.VisualState switch
    {
        VisualState.Loading => "Claude Usage — Loading...",
        VisualState.Disconnected => "Claude Usage — Disconnected",
        VisualState.Error => $"Claude Usage — Error: {state.LastError}",
        _ => $"Claude Usage | Session {state.Metrics.SessionUsedPercent:F0}% | Weekly {state.Metrics.WeeklyUsedPercent:F0}%"
    };

    private static Icon LoadIcon(VisualState state)
    {
        // Generate a small colored icon programmatically so we don't need .ico files
        var color = state switch
        {
            VisualState.Normal => Color.FromArgb(0x4C, 0xC9, 0x74),
            VisualState.Warning => Color.FromArgb(0xFF, 0xB9, 0x00),
            VisualState.Critical => Color.FromArgb(0xFF, 0x45, 0x45),
            VisualState.Disconnected => Color.FromArgb(0x88, 0x88, 0x88),
            VisualState.Error => Color.FromArgb(0xFF, 0x45, 0x45),
            _ => Color.FromArgb(0x60, 0xCD, 0xEA) // Loading = blue
        };
        return CreateColorIcon(color, 16);
    }

    private static Icon CreateColorIcon(Color fillColor, int size)
    {
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Outer circle
        using var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 40));
        g.FillEllipse(bgBrush, 0, 0, size - 1, size - 1);

        // Inner dot
        int margin = size / 4;
        using var fill = new SolidBrush(fillColor);
        g.FillEllipse(fill, margin, margin, size - margin * 2 - 1, size - margin * 2 - 1);

        return Icon.FromHandle(bmp.GetHicon());
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var refreshItem = new ToolStripMenuItem("Refresh Now");
        refreshItem.Click += async (_, _) =>
        {
            try { await _sync.ForceSyncAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Force sync failed"); }
        };

        var toggleItem = new ToolStripMenuItem("Toggle Widget");
        toggleItem.Click += (_, _) => ToggleWidget();

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => OpenSettings();

        menu.Items.Add(refreshItem);
        menu.Items.Add(toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        return menu;
    }

    private void ToggleWidget()
    {
        _widgetVisible = !_widgetVisible;
        if (_widgetVisible)
            _widget.Show();
        else
            _widget.Hide();
    }

    private void OpenSettings()
    {
        _settingsWindow.ShowDialog();
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
