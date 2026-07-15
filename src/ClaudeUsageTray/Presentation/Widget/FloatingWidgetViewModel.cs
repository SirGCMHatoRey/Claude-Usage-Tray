using System.ComponentModel;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using ClaudeUsageTray.Application.State;
using ClaudeUsageTray.Domain.Models;
using VisualState = ClaudeUsageTray.Domain.Models.VisualState;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTray.Presentation.Widget;

public sealed class FloatingWidgetViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDisposable _subscription;
    private AppState _state = AppState.Initial;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public FloatingWidgetViewModel(IUsageStore store, ILogger<FloatingWidgetViewModel> logger)
    {
        _subscription = store.StateStream
            .Subscribe(s =>
            {
                if (_dispatcher.CheckAccess())
                {
                    _state = s;
                    RaiseAll();
                }
                else
                {
                    _dispatcher.Invoke(() => { _state = s; RaiseAll(); });
                }
            });
    }

    public double SessionPercent => _state.Metrics.SessionUsedPercent;
    public double WeeklyPercent => _state.Metrics.WeeklyUsedPercent;

    public string SessionLabel => _state.Metrics.HasData
        ? $"{_state.Metrics.SessionUsedPercent:F0}%"
        : "--";

    public string WeeklyLabel => _state.Metrics.HasData
        ? $"{_state.Metrics.WeeklyUsedPercent:F0}%"
        : "--";

    public string SessionDetail => _state.Metrics.HasData
        ? $"{FormatTokens(_state.Metrics.SessionTokensUsed)} / {FormatTokens(_state.Metrics.SessionTokensLimit)}"
        : "Loading...";

    public string WeeklyDetail => _state.Metrics.HasData
        ? $"{FormatTokens(_state.Metrics.WeeklyTokensUsed)} / {FormatTokens(_state.Metrics.WeeklyTokensLimit)}"
        : "Loading...";

    public string StatusText => _state.VisualState switch
    {
        VisualState.Loading => "Connecting...",
        VisualState.Disconnected => "Disconnected",
        VisualState.Error => $"Error: {_state.LastError}",
        _ => _state.LastUpdated == default ? "" : $"Updated {_state.LastUpdated:HH:mm}"
    };

    public VisualState VisualState => _state.VisualState;

    public bool IsLoading => _state.VisualState == VisualState.Loading;
    public bool HasError => _state.VisualState is VisualState.Error or VisualState.Disconnected;

    public string ResetLabel => _state.Metrics.ResetAt > DateTimeOffset.UtcNow
        ? $"Resets {FormatRelative(_state.Metrics.ResetAt)}"
        : "";

    private static string FormatTokens(long tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000.0:F1}M"
        : tokens >= 1_000 ? $"{tokens / 1_000.0:F0}k"
        : tokens.ToString();

    private static string FormatRelative(DateTimeOffset at)
    {
        var diff = at - DateTimeOffset.UtcNow;
        if (diff.TotalDays >= 1) return $"in {(int)diff.TotalDays}d";
        if (diff.TotalHours >= 1) return $"in {(int)diff.TotalHours}h";
        return $"in {(int)diff.TotalMinutes}m";
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(SessionPercent));
        OnPropertyChanged(nameof(WeeklyPercent));
        OnPropertyChanged(nameof(SessionLabel));
        OnPropertyChanged(nameof(WeeklyLabel));
        OnPropertyChanged(nameof(SessionDetail));
        OnPropertyChanged(nameof(WeeklyDetail));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(VisualState));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ResetLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose() => _subscription.Dispose();
}
