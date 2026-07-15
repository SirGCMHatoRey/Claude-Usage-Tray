using System.Reactive.Linq;
using System.Reactive.Subjects;
using ClaudeUsageTray.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTray.Application.State;

public interface IUsageStore
{
    IObservable<AppState> StateStream { get; }
    AppState CurrentState { get; }
    void Dispatch(AppStateUpdate update);
}

public sealed class UsageStore : IUsageStore, IDisposable
{
    private readonly BehaviorSubject<AppState> _subject;
    private readonly ILogger<UsageStore> _logger;
    private readonly object _lock = new();

    public UsageStore(ILogger<UsageStore> logger)
    {
        _logger = logger;
        _subject = new BehaviorSubject<AppState>(AppState.Initial);
    }

    public IObservable<AppState> StateStream => _subject.AsObservable();
    public AppState CurrentState => _subject.Value;

    public void Dispatch(AppStateUpdate update)
    {
        lock (_lock)
        {
            var current = _subject.Value;
            var next = update.Apply(current);
            if (ReferenceEquals(next, current)) return;
            _logger.LogDebug("State → {State} (connected={Connected})", next.VisualState, next.IsConnected);
            _subject.OnNext(next);
        }
    }

    public void Dispose() => _subject.Dispose();
}

// ── Update commands (discriminated union via abstract base) ──────────────────

public abstract class AppStateUpdate
{
    public abstract AppState Apply(AppState current);
}

public sealed class MetricsUpdated(UsageMetrics metrics) : AppStateUpdate
{
    public override AppState Apply(AppState current) => current with
    {
        Metrics = metrics,
        VisualState = metrics.VisualState,
        IsConnected = true,
        LastError = null,
        LastUpdated = DateTimeOffset.UtcNow
    };
}

public sealed class SyncFailed(string error, bool disconnected = false) : AppStateUpdate
{
    public override AppState Apply(AppState current) => current with
    {
        VisualState = disconnected ? VisualState.Disconnected : VisualState.Error,
        IsConnected = !disconnected,
        LastError = error,
        LastUpdated = DateTimeOffset.UtcNow
    };
}

public sealed class SyncStarted : AppStateUpdate
{
    public static readonly SyncStarted Instance = new();
    public override AppState Apply(AppState current) =>
        current.IsConnected ? current : current with { VisualState = VisualState.Loading };
}
