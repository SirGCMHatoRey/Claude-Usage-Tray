using ClaudeUsageTray.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTray.Infrastructure.Cache;

public interface IUsageCache
{
    void Store(UsageMetrics metrics);
    bool TryGetLastGood(out UsageMetrics? metrics);
    void Invalidate();
}

public sealed class UsageCache : IUsageCache
{
    private readonly ILogger<UsageCache> _logger;
    private volatile UsageMetrics? _cached;
    private readonly TimeSpan _maxStaleness = TimeSpan.FromHours(2);

    public UsageCache(ILogger<UsageCache> logger) => _logger = logger;

    public void Store(UsageMetrics metrics)
    {
        _cached = metrics;
        _logger.LogDebug("Usage cache updated at {At}", metrics.FetchedAt);
    }

    public bool TryGetLastGood(out UsageMetrics? metrics)
    {
        metrics = _cached;
        if (metrics is null) return false;
        if (DateTimeOffset.UtcNow - metrics.FetchedAt > _maxStaleness)
        {
            _logger.LogWarning("Cache entry is stale (>{Max}h old). Returning anyway as fallback.",
                _maxStaleness.TotalHours);
        }
        return true;
    }

    public void Invalidate()
    {
        _cached = null;
        _logger.LogDebug("Usage cache invalidated");
    }
}
