namespace ClaudeUsageTray.Domain.Models;

public sealed record UsageMetrics
{
    public static readonly UsageMetrics Empty = new();

    public double SessionUsedPercent { get; init; }
    public double WeeklyUsedPercent { get; init; }
    public long SessionTokensUsed { get; init; }
    public long SessionTokensLimit { get; init; }
    public long WeeklyTokensUsed { get; init; }
    public long WeeklyTokensLimit { get; init; }
    public DateTimeOffset ResetAt { get; init; }
    public DateTimeOffset FetchedAt { get; init; }
    public VisualState VisualState { get; init; }

    public bool HasData => SessionTokensLimit > 0 || WeeklyTokensLimit > 0;
}

public enum VisualState
{
    Loading,
    Normal,
    Warning,
    Critical,
    Disconnected,
    Error
}
