using System.Globalization;
using System.Text.Json;
using QuotaRing.Models;

namespace QuotaRing.Services;

public static class QuotaParsers
{
    private static readonly (string Id, string Label, string SubLabel)[] AntigravityOrder =
    [
        ("gemini-5h", "5h", "Gemini"),
        ("gemini-weekly", "7d", "Gemini"),
        ("3p-5h", "5h", "Claude/GPT"),
        ("3p-weekly", "7d", "Claude/GPT"),
    ];

    public static IReadOnlyList<QuotaRingItem> ParseAntigravityJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("status", out var statusEl) ||
            !string.Equals(statusEl.GetString(), "SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Antigravity CLI did not return SUCCESS status.");
        }

        if (!root.TryGetProperty("command", out var cmdEl) ||
            !cmdEl.TryGetProperty("data", out var dataEl) ||
            !dataEl.TryGetProperty("groups", out var groupsEl) ||
            groupsEl.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Antigravity quota groups are missing.");
        }

        var found = new Dictionary<string, (double Remaining, DateTimeOffset? Reset)>(StringComparer.Ordinal);

        foreach (var group in groupsEl.EnumerateArray())
        {
            if (!group.TryGetProperty("buckets", out var bucketsEl) ||
                bucketsEl.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var bucket in bucketsEl.EnumerateArray())
            {
                if (bucket.TryGetProperty("disabled", out var disabledEl) &&
                    disabledEl.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                if (!bucket.TryGetProperty("id", out var idEl) ||
                    idEl.GetString() is not { Length: > 0 } id)
                {
                    continue;
                }

                if (!bucket.TryGetProperty("remaining_fraction", out var fracEl) ||
                    fracEl.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var fraction = fracEl.GetDouble();
                if (double.IsNaN(fraction) || double.IsInfinity(fraction))
                {
                    continue;
                }

                var remainingPct = Math.Clamp(fraction * 100.0, 0.0, 100.0);
                DateTimeOffset? resetAt = null;
                if (bucket.TryGetProperty("reset_time", out var resetEl) &&
                    resetEl.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(
                        resetEl.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsedReset))
                {
                    resetAt = parsedReset;
                }

                if (!found.TryGetValue(id, out var existing) || remainingPct < existing.Remaining)
                {
                    found[id] = (remainingPct, resetAt);
                }
            }
        }

        var result = new List<QuotaRingItem>();
        foreach (var (id, label, subLabel) in AntigravityOrder)
        {
            if (found.TryGetValue(id, out var entry))
            {
                result.Add(new QuotaRingItem(id, label, subLabel, entry.Remaining, entry.Reset));
            }
        }

        if (result.Count == 0)
        {
            throw new FormatException("Antigravity returned no supported quota buckets.");
        }

        return result;
    }

    public static (string? PlanLabel, IReadOnlyList<QuotaRingItem> Rings) ParseCodexJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? planLabel = null;
        if (root.TryGetProperty("plan_type", out var planEl) && planEl.ValueKind == JsonValueKind.String)
        {
            var rawPlan = planEl.GetString()?.Trim();
            if (!string.IsNullOrEmpty(rawPlan))
            {
                planLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(rawPlan.ToLowerInvariant());
            }
        }

        if (!root.TryGetProperty("rate_limit", out var rateEl) || rateEl.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Codex response is missing rate_limit.");
        }

        var rings = new List<QuotaRingItem>();
        TryAddCodexWindow(rateEl, "primary_window", 18000, rings);
        TryAddCodexWindow(rateEl, "secondary_window", 604800, rings);

        if (rings.Count == 0)
        {
            throw new FormatException("Codex response contained no quota windows.");
        }

        return (planLabel, rings);
    }

    private static void TryAddCodexWindow(
        JsonElement rateEl,
        string propertyName,
        int defaultSeconds,
        List<QuotaRingItem> rings)
    {
        if (!rateEl.TryGetProperty(propertyName, out var winEl) || winEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!winEl.TryGetProperty("used_percent", out var usedEl) || usedEl.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        var usedPct = Math.Clamp(usedEl.GetDouble(), 0.0, 100.0);
        var remainingPct = Math.Clamp(100.0 - usedPct, 0.0, 100.0);

        var seconds = defaultSeconds;
        if (winEl.TryGetProperty("limit_window_seconds", out var secEl) && secEl.ValueKind == JsonValueKind.Number)
        {
            var val = secEl.GetInt32();
            if (val > 0)
            {
                seconds = val;
            }
        }

        var (id, label) = seconds switch
        {
            18000 => ("five_hour", "5h"),
            604800 => ("seven_day", "7d"),
            _ when seconds % 86400 == 0 => ($"d{seconds / 86400}", $"{seconds / 86400}d"),
            _ when seconds % 3600 == 0 => ($"h{seconds / 3600}", $"{seconds / 3600}h"),
            _ => ($"m{Math.Max(1, seconds / 60)}", $"{Math.Max(1, seconds / 60)}m")
        };

        DateTimeOffset? resetAt = null;
        if (winEl.TryGetProperty("reset_at", out var resetEl))
        {
            if (resetEl.ValueKind == JsonValueKind.Number && resetEl.TryGetInt64(out var unixSec) && unixSec > 0)
            {
                resetAt = DateTimeOffset.FromUnixTimeSeconds(unixSec);
            }
            else if (resetEl.ValueKind == JsonValueKind.String &&
                     DateTimeOffset.TryParse(
                         resetEl.GetString(),
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                         out var parsed))
            {
                resetAt = parsed;
            }
        }

        rings.Add(new QuotaRingItem(id, label, null, remainingPct, resetAt));
    }

    public static IReadOnlyList<QuotaRingItem> ParseClaudeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var rings = new List<QuotaRingItem>();
        TryAddClaudeWindow(root, "five_hour", "5h", rings);
        TryAddClaudeWindow(root, "seven_day", "7d", rings);

        if (rings.Count == 0)
        {
            throw new FormatException("Claude response contained no five_hour or seven_day quota windows.");
        }

        return rings;
    }

    private static void TryAddClaudeWindow(
        JsonElement root,
        string propertyName,
        string label,
        List<QuotaRingItem> rings)
    {
        if (!root.TryGetProperty(propertyName, out var winEl) || winEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!winEl.TryGetProperty("utilization", out var utilEl) || utilEl.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        var usedPct = Math.Clamp(utilEl.GetDouble(), 0.0, 100.0);
        var remainingPct = Math.Clamp(100.0 - usedPct, 0.0, 100.0);

        DateTimeOffset? resetAt = null;
        if (winEl.TryGetProperty("resets_at", out var resetEl) &&
            resetEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                resetEl.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            resetAt = parsed;
        }

        rings.Add(new QuotaRingItem(propertyName, label, null, remainingPct, resetAt));
    }

    public static string FormatCountdown(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null)
        {
            return "";
        }

        var diff = resetsAt.Value - now;
        if (diff <= TimeSpan.Zero)
        {
            return "即将重置";
        }

        if (diff.TotalDays >= 1)
        {
            return $"{(int)diff.TotalDays}d {diff.Hours}h";
        }

        if (diff.TotalHours >= 1)
        {
            return $"{(int)diff.TotalHours}h {diff.Minutes}m";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes))}m";
    }

    public static string GetRingColorHex(double remainingPercent)
    {
        var pct = Math.Clamp(remainingPercent, 0.0, 100.0);
        if (pct <= 15.0)
        {
            return "#EF4444"; // Red
        }

        if (pct <= 35.0)
        {
            return "#F59E0B"; // Amber
        }

        return "#10B981"; // Emerald
    }
}
