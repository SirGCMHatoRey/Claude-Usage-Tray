namespace ClaudeUsageTray.Domain.Models;

public sealed record AppState
{
    public static readonly AppState Initial = new()
    {
        Metrics = UsageMetrics.Empty,
        VisualState = VisualState.Loading,
        LastError = null,
        IsConnected = false
    };

    public required UsageMetrics Metrics { get; init; }
    public required VisualState VisualState { get; init; }
    public string? LastError { get; init; }
    public bool IsConnected { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}
