using QuotaRing.Services;

var passed = 0;
var failed = 0;

void Assert(bool condition, string name)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"[PASS] {name}");
    }
    else
    {
        failed++;
        Console.Error.WriteLine($"[FAIL] {name}");
    }
}

void AssertThrows<TEx>(Action action, string name) where TEx : Exception
{
    try
    {
        action();
        failed++;
        Console.Error.WriteLine($"[FAIL] {name} (expected {typeof(TEx).Name})");
    }
    catch (TEx)
    {
        passed++;
        Console.WriteLine($"[PASS] {name}");
    }
}

// 1. Antigravity JSON parsing
var agyJson = """
{
  "status": "SUCCESS",
  "command": {
    "name": "usage",
    "data": {
      "groups": [
        {
          "name": "Gemini Models",
          "buckets": [
            { "id": "gemini-weekly", "remaining_fraction": 0.9187, "reset_time": "2026-12-02T00:28:15Z" },
            { "id": "gemini-5h", "remaining_fraction": 0.9044, "reset_time": "2026-11-25T13:52:12Z" }
          ]
        },
        {
          "name": "Claude and GPT models",
          "buckets": [
            { "id": "3p-weekly", "remaining_fraction": 1.0, "reset_time": "2026-12-02T09:30:56Z" },
            { "id": "3p-5h", "remaining_fraction": 0.25, "reset_time": "2026-11-25T14:30:56Z" }
          ]
        }
      ]
    }
  }
}
""";
var agyRings = QuotaParsers.ParseAntigravityJson(agyJson);
Assert(agyRings.Count == 4, "Antigravity parses all 4 buckets");
Assert(agyRings[0].Id == "gemini-5h" && Math.Abs(agyRings[0].RemainingPercent - 90.44) < 0.01, "Antigravity orders gemini-5h first with remaining %");
Assert(agyRings[2].Id == "3p-5h" && Math.Abs(agyRings[2].UsedPercent - 75.0) < 0.01, "Antigravity computes used % from remaining %");

// 2. Codex JSON parsing
var codexJson = """
{
  "plan_type": "plus",
  "rate_limit": {
    "primary_window": { "used_percent": 26.0, "limit_window_seconds": 18000, "reset_at": 1795614049 },
    "secondary_window": { "used_percent": 34.0, "limit_window_seconds": 604800, "reset_at": 1796128656 }
  }
}
""";
var (plan, codexRings) = QuotaParsers.ParseCodexJson(codexJson);
Assert(plan == "Plus", "Codex formats plan title case");
Assert(codexRings.Count == 2, "Codex parses 5h and 7d windows");
Assert(codexRings[0].Label == "5h" && Math.Abs(codexRings[0].RemainingPercent - 74.0) < 0.01, "Codex 5h converts used_percent to remaining_percent");
Assert(codexRings[1].Label == "7d" && Math.Abs(codexRings[1].RemainingPercent - 66.0) < 0.01, "Codex 7d converts used_percent to remaining_percent");

// 3. Claude JSON parsing
var claudeJson = """
{
  "five_hour": { "utilization": 42.5, "resets_at": "2026-11-25T15:00:00Z" },
  "seven_day": { "utilization": 18.0, "resets_at": "2026-12-01T10:00:00Z" }
}
""";
var claudeRings = QuotaParsers.ParseClaudeJson(claudeJson);
Assert(claudeRings.Count == 2, "Claude parses five_hour and seven_day");
Assert(Math.Abs(claudeRings[0].RemainingPercent - 57.5) < 0.01, "Claude 5h remaining % accurate");
Assert(Math.Abs(claudeRings[1].RemainingPercent - 82.0) < 0.01, "Claude 7d remaining % accurate");

// 4. Ring color thresholds
Assert(QuotaParsers.GetRingColorHex(85) == "#10B981", "High remaining uses emerald");
Assert(QuotaParsers.GetRingColorHex(30) == "#F59E0B", "Medium-low remaining uses amber");
Assert(QuotaParsers.GetRingColorHex(10) == "#EF4444", "Critical remaining uses red");

// 5. Countdown formatting
var now = DateTimeOffset.Parse("2026-11-25T10:00:00Z");
Assert(QuotaParsers.FormatCountdown(now.AddDays(2).AddHours(3), now) == "2d 3h", "Countdown formats days + hours");
Assert(QuotaParsers.FormatCountdown(now.AddHours(4).AddMinutes(21), now) == "4h 21m", "Countdown formats hours + minutes");
Assert(QuotaParsers.FormatCountdown(now.AddMinutes(-1), now) == "即将重置", "Past reset shows imminent reset");

// 6. Invalid payloads fail cleanly instead of silent fallback
AssertThrows<FormatException>(() => QuotaParsers.ParseAntigravityJson("""{"status":"FAILED"}"""), "Antigravity rejects non-SUCCESS status");
AssertThrows<FormatException>(() => QuotaParsers.ParseCodexJson("""{}"""), "Codex rejects missing rate_limit");
AssertThrows<FormatException>(() => QuotaParsers.ParseClaudeJson("""{}"""), "Claude rejects missing windows");

Console.WriteLine($"\nSummary: {passed} passed, {failed} failed.");
Environment.Exit(failed == 0 ? 0 : 1);
