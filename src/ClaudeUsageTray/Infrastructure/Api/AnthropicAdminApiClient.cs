using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeUsageTray.Domain.Models;
using ClaudeUsageTray.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTray.Infrastructure.Api;

public interface IAnthropicAdminApiClient
{
    Task<UsageMetrics> GetUsageMetricsAsync(CancellationToken ct = default);
}

public sealed class AnthropicAdminApiClient : IAnthropicAdminApiClient
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly ICredentialStore _credentials;
    private readonly ILogger<AnthropicAdminApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicAdminApiClient(
        HttpClient http,
        ICredentialStore credentials,
        ILogger<AnthropicAdminApiClient> logger)
    {
        _http = http;
        _credentials = credentials;
        _logger = logger;
    }

    public async Task<UsageMetrics> GetUsageMetricsAsync(CancellationToken ct = default)
    {
        if (!_credentials.TryGetApiKey(out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Admin API key configured.");

        using var request = new HttpRequestMessage(HttpMethod.Get, "usage");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogDebug("Fetching usage metrics from Anthropic Admin API");

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Admin API key rejected (401). Check key in settings.");

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Rate limited (429). Will retry with backoff.");

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _logger.LogDebug("Usage API response received ({Bytes} bytes)", json.Length);

        return ParseUsageResponse(json);
    }

    private UsageMetrics ParseUsageResponse(string json)
    {
        var root = JsonSerializer.Deserialize<UsageResponse>(json, JsonOpts)
            ?? throw new JsonException("Null usage response from API");

        var sessionUsed = root.Session?.TokensUsed ?? 0L;
        var sessionLimit = root.Session?.TokensLimit ?? 0L;
        var weeklyUsed = root.Weekly?.TokensUsed ?? 0L;
        var weeklyLimit = root.Weekly?.TokensLimit ?? 0L;

        var sessionPct = sessionLimit > 0 ? (double)sessionUsed / sessionLimit * 100.0 : 0;
        var weeklyPct = weeklyLimit > 0 ? (double)weeklyUsed / weeklyLimit * 100.0 : 0;

        var state = ComputeVisualState(sessionPct, weeklyPct);

        return new UsageMetrics
        {
            SessionTokensUsed = sessionUsed,
            SessionTokensLimit = sessionLimit,
            SessionUsedPercent = sessionPct,
            WeeklyTokensUsed = weeklyUsed,
            WeeklyTokensLimit = weeklyLimit,
            WeeklyUsedPercent = weeklyPct,
            ResetAt = root.Session?.ResetsAt ?? DateTimeOffset.UtcNow.AddDays(1),
            FetchedAt = DateTimeOffset.UtcNow,
            VisualState = state
        };
    }

    private static VisualState ComputeVisualState(double sessionPct, double weeklyPct)
    {
        var max = Math.Max(sessionPct, weeklyPct);
        return max switch
        {
            >= 90 => VisualState.Critical,
            >= 70 => VisualState.Warning,
            _ => VisualState.Normal
        };
    }

    // ── Response DTOs ─────────────────────────────────────────────────────────

    private sealed class UsageResponse
    {
        [JsonPropertyName("session")] public UsagePeriod? Session { get; set; }
        [JsonPropertyName("weekly")] public UsagePeriod? Weekly { get; set; }
    }

    private sealed class UsagePeriod
    {
        [JsonPropertyName("tokens_used")] public long TokensUsed { get; set; }
        [JsonPropertyName("tokens_limit")] public long TokensLimit { get; set; }
        [JsonPropertyName("resets_at")] public DateTimeOffset ResetsAt { get; set; }
    }
}
