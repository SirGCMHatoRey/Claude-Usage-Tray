using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeUsageTray.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTray.Infrastructure.ClaudeAi;

public interface IClaudeAiUsageProvider
{
    Task<UsageMetrics> GetUsageMetricsAsync(string sessionKey, CancellationToken ct = default);
}

public sealed class ClaudeAiUsageProvider : IClaudeAiUsageProvider
{
    private readonly IWebViewApiGateway _gateway;
    private readonly ILogger<ClaudeAiUsageProvider> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ClaudeAiUsageProvider(IWebViewApiGateway gateway, ILogger<ClaudeAiUsageProvider> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<UsageMetrics> GetUsageMetricsAsync(string sessionKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            throw new InvalidOperationException("No Claude.ai session key configured.");

        var orgId = await GetOrganizationIdAsync(ct);
        _logger.LogDebug("Resolved org ID: {OrgId}", orgId);

        return await GetLimitsAsync(orgId, ct);
    }

    private async Task<string> GetOrganizationIdAsync(CancellationToken ct)
    {
        var (status, json) = await _gateway.GetJsonAsync("/api/organizations", ct);
        if (status != 200)
        {
            _logger.LogWarning("GET /api/organizations → {Status}. Body: {Body}",
                status, json.Length > 600 ? json[..600] : json);
            throw new HttpRequestException($"GET /api/organizations returned {status}", null,
                (System.Net.HttpStatusCode)Math.Clamp(status, 100, 599));
        }

        _logger.LogDebug("Organizations response: {Json}", json.Length > 500 ? json[..500] : json);

        var orgs = JsonSerializer.Deserialize<List<OrganizationDto>>(json, JsonOpts);
        var org = orgs?.FirstOrDefault()
            ?? throw new InvalidOperationException("No organizations found in claude.ai response.");

        return org.Uuid ?? org.Id ?? throw new InvalidOperationException("Organization has no ID.");
    }

    private async Task<UsageMetrics> GetLimitsAsync(string orgId, CancellationToken ct)
    {
        var candidates = new[]
        {
            $"/api/organizations/{orgId}/usage",
            $"/api/organizations/{orgId}/limits",
            $"/api/organizations/{orgId}/entitlements",
            $"/api/organizations/{orgId}",
        };

        foreach (var path in candidates)
        {
            var (status, json) = await _gateway.GetJsonAsync(path, ct);
            if (status != 200) continue;

            _logger.LogDebug("{Path} => {Json}", path, json.Length > 800 ? json[..800] : json);

            var metrics = TryParseUsageMetrics(json, path);
            if (metrics is not null) return metrics;
        }

        throw new InvalidOperationException(
            "Could not find usage data from any claude.ai endpoint. Check logs for raw responses.");
    }

    private UsageMetrics? TryParseUsageMetrics(string json, string sourceEndpoint)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var sessionUsed = TryGetLong(root, "message_count", "messages_used", "session_messages_used", "used");
            var sessionLimit = TryGetLong(root, "message_limit", "messages_limit", "session_messages_limit", "limit");
            var weeklyUsed = TryGetLong(root, "weekly_messages_used", "weekly_used");
            var weeklyLimit = TryGetLong(root, "weekly_messages_limit", "weekly_limit");

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    sessionUsed ??= TryGetLong(prop.Value, "message_count", "messages_used", "used");
                    sessionLimit ??= TryGetLong(prop.Value, "message_limit", "messages_limit", "limit");
                    weeklyUsed ??= TryGetLong(prop.Value, "weekly_messages_used", "weekly_used");
                    weeklyLimit ??= TryGetLong(prop.Value, "weekly_messages_limit", "weekly_limit");
                }
            }

            if (sessionLimit is null && weeklyLimit is null)
            {
                _logger.LogDebug("No usage fields found at {Endpoint}", sourceEndpoint);
                return null;
            }

            var sUsed = sessionUsed ?? 0;
            var sLimit = sessionLimit ?? 0;
            var wUsed = weeklyUsed ?? 0;
            var wLimit = weeklyLimit ?? 0;

            var sPct = sLimit > 0 ? (double)sUsed / sLimit * 100.0 : 0;
            var wPct = wLimit > 0 ? (double)wUsed / wLimit * 100.0 : 0;

            return new UsageMetrics
            {
                SessionTokensUsed = sUsed,
                SessionTokensLimit = sLimit,
                SessionUsedPercent = sPct,
                WeeklyTokensUsed = wUsed,
                WeeklyTokensLimit = wLimit,
                WeeklyUsedPercent = wPct,
                FetchedAt = DateTimeOffset.UtcNow,
                VisualState = ComputeState(sPct, wPct)
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse usage JSON from {Endpoint}", sourceEndpoint);
            return null;
        }
    }

    private static long? TryGetLong(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var p) && p.TryGetInt64(out var v))
                return v;
        }
        return null;
    }

    private static VisualState ComputeState(double s, double w) =>
        Math.Max(s, w) switch
        {
            >= 90 => VisualState.Critical,
            >= 70 => VisualState.Warning,
            _ => VisualState.Normal
        };

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private sealed class OrganizationDto
    {
        [JsonPropertyName("uuid")] public string? Uuid { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
