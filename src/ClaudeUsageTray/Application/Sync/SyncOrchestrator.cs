using ClaudeUsageTray.Application.State;
using ClaudeUsageTray.Domain.Models;
using ClaudeUsageTray.Infrastructure.Cache;
using ClaudeUsageTray.Infrastructure.ClaudeAi;
using ClaudeUsageTray.Infrastructure.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTray.Application.Sync;

public sealed class SyncOrchestrator : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly IClaudeAiUsageProvider _provider;
    private readonly ICredentialStore _credentials;
    private readonly IUsageStore _store;
    private readonly IUsageCache _cache;
    private readonly ILogger<SyncOrchestrator> _logger;

    private int _consecutiveFailures;
    private const int MaxBeforeDisconnected = 3;

    public SyncOrchestrator(
        IClaudeAiUsageProvider provider,
        ICredentialStore credentials,
        IUsageStore store,
        IUsageCache cache,
        ILogger<SyncOrchestrator> logger)
    {
        _provider = provider;
        _credentials = credentials;
        _store = store;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncOrchestrator started (interval={Interval})", PollingInterval);

        if (_cache.TryGetLastGood(out var cached) && cached is not null)
            _store.Dispatch(new MetricsUpdated(cached));

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncOnceAsync(stoppingToken).ConfigureAwait(false);

            var delay = _consecutiveFailures > 0 ? RetryDelay : PollingInterval;
            _logger.LogDebug("Next sync in {Delay}", delay);
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task ForceSyncAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Force sync requested");
        await SyncOnceAsync(ct).ConfigureAwait(false);
    }

    private async Task SyncOnceAsync(CancellationToken ct)
    {
        _store.Dispatch(SyncStarted.Instance);

        if (!_credentials.TryGetSessionKey(out var sessionKey) || string.IsNullOrWhiteSpace(sessionKey))
        {
            _store.Dispatch(new SyncFailed("No Claude.ai session configured. Open Settings to sign in.", disconnected: false));
            return;
        }

        try
        {
            var metrics = await _provider.GetUsageMetricsAsync(sessionKey!, ct).ConfigureAwait(false);
            _cache.Store(metrics);
            _store.Dispatch(new MetricsUpdated(metrics));
            _consecutiveFailures = 0;
            _logger.LogInformation("Sync OK — session={S:F1}% weekly={W:F1}%",
                metrics.SessionUsedPercent, metrics.WeeklyUsedPercent);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            bool disconnected = _consecutiveFailures >= MaxBeforeDisconnected;
            _logger.LogWarning(ex, "Sync failed (attempt #{N})", _consecutiveFailures);

            if (_cache.TryGetLastGood(out var stale) && stale is not null)
                _store.Dispatch(new MetricsUpdated(stale with { VisualState = VisualState.Disconnected }));
            else
                _store.Dispatch(new SyncFailed(ex.Message, disconnected));
        }
    }
}
