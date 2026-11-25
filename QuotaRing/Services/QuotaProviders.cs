using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using QuotaRing.Models;

namespace QuotaRing.Services;

public sealed class QuotaProviders : IDisposable
{
    private static readonly Uri CodexUsageUri = new("https://chatgpt.com/backend-api/wham/usage");
    private static readonly Uri ClaudeUsageUri = new("https://api.anthropic.com/api/oauth/usage");

    private readonly HttpClient _http;
    private readonly string _userProfile;
    private readonly string _localAppData;

    public QuotaProviders()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("QuotaRing/1.0");
        _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public async Task<ProviderSnapshot> FetchAntigravityAsync(CancellationToken ct)
    {
        const string id = "antigravity";
        const string name = "Antigravity";
        const string accent = "#6366F1";

        var agyPath = FindAgyBinary();
        if (agyPath is null)
        {
            return new ProviderSnapshot(
                id, name, accent, null,
                ProviderStatus.NotSignedIn,
                [],
                "未找到 agy CLI",
                DateTimeOffset.Now);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = agyPath,
                Arguments = "-p /usage --output-format json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start agy CLI.");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));
            await proc.WaitForExitAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? $"退出码 {proc.ExitCode}" : stderr.Trim();
                return new ProviderSnapshot(
                    id, name, accent, null,
                    ProviderStatus.Error,
                    [],
                    TruncateError(detail),
                    DateTimeOffset.Now);
            }

            var rings = QuotaParsers.ParseAntigravityJson(stdout);
            return new ProviderSnapshot(
                id, name, accent, null,
                ProviderStatus.Ready,
                rings,
                null,
                DateTimeOffset.Now);
        }
        catch (OperationCanceledException)
        {
            return new ProviderSnapshot(
                id, name, accent, null,
                ProviderStatus.Error,
                [],
                "查询超时",
                DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            return new ProviderSnapshot(
                id, name, accent, null,
                ProviderStatus.Error,
                [],
                TruncateError(ex.Message),
                DateTimeOffset.Now);
        }
    }

    public async Task<ProviderSnapshot> FetchCodexAsync(CancellationToken ct)
    {
        const string id = "codex";
        const string name = "Codex";
        const string accent = "#10B981";

        var authPath = Path.Combine(_userProfile, ".codex", "auth.json");
        if (!File.Exists(authPath))
        {
            return new ProviderSnapshot(
                id, name, accent, null,
                ProviderStatus.NotSignedIn,
                [],
                "未登录 Codex",
                DateTimeOffset.Now);
        }

        try
        {
            var raw = await File.ReadAllTextAsync(authPath, ct);
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("tokens", out var tokensEl) ||
                !tokensEl.TryGetProperty("access_token", out var accessEl) ||
                accessEl.GetString() is not { Length: > 0 } accessToken)
            {
                return new ProviderSnapshot(
                    id, name, accent, null,
                    ProviderStatus.NotSignedIn,
                    [],
                    "未登录 Codex",
                    DateTimeOffset.Now);
            }

            string? accountId = null;
            if (tokensEl.TryGetProperty("account_id", out var accEl) && accEl.ValueKind == JsonValueKind.String)
            {
                accountId = accEl.GetString();
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, CodexUsageUri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                req.Headers.TryAddWithoutValidation("ChatGPT-Account-ID", accountId);
            }

            using var resp = await _http.SendAsync(req, ct);
            if ((int)resp.StatusCode is 401 or 403)
            {
                return new ProviderSnapshot(
                    id, name, accent, null,
                    ProviderStatus.NotSignedIn,
                    [],
                    "登录已过期，请打开 Codex 刷新",
                    DateTimeOffset.Now);
            }

            if (!resp.IsSuccessStatusCode)
            {
                return new ProviderSnapshot(
                    id, name, accent, null,
                    ProviderStatus.Error,
                    [],
                    $"HTTP {(int)resp.StatusCode}",
                    DateTimeOffset.Now);
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            var (planLabel, rings) = QuotaParsers.ParseCodexJson(body);
            return new ProviderSnapshot(
                id, name, accent, planLabel,
                ProviderStatus.Ready,
                rings,
                null,
                DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            return new ProviderSnapshot(
                id, name, accent, null,
                ProviderStatus.Error,
                [],
                TruncateError(ex.Message),
                DateTimeOffset.Now);
        }
    }

    public async Task<ProviderSnapshot> FetchClaudeAsync(CancellationToken ct)
    {
        const string id = "claude";
        const string name = "Claude";
        const string accent = "#F97316";

        var credPath = Path.Combine(_userProfile, ".claude", ".credentials.json");
        if (!File.Exists(credPath))
        {
            return new ProviderSnapshot(
                id, name, accent, null,
                ProviderStatus.NotSignedIn,
                [],
                "未登录（运行 claude 登录）",
                DateTimeOffset.Now);
        }

        try
        {
            var raw = await File.ReadAllTextAsync(credPath, ct);
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauthEl) ||
                oauthEl.ValueKind != JsonValueKind.Object ||
                !oauthEl.TryGetProperty("accessToken", out var accessEl) ||
                accessEl.GetString() is not { Length: > 0 } accessToken)
            {
                return new ProviderSnapshot(
                    id, name, accent, null,
                    ProviderStatus.NotSignedIn,
                    [],
                    "未登录（运行 claude 登录）",
                    DateTimeOffset.Now);
            }

            string? planLabel = null;
            if (oauthEl.TryGetProperty("subscriptionType", out var subEl) && subEl.ValueKind == JsonValueKind.String)
            {
                var sub = subEl.GetString()?.Trim();
                if (!string.IsNullOrEmpty(sub))
                {
                    planLabel = sub;
                }
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, ClaudeUsageUri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            using var resp = await _http.SendAsync(req, ct);
            if ((int)resp.StatusCode is 401 or 403)
            {
                return new ProviderSnapshot(
                    id, name, accent, planLabel,
                    ProviderStatus.NotSignedIn,
                    [],
                    "登录已过期（运行 claude 刷新）",
                    DateTimeOffset.Now);
            }

            if ((int)resp.StatusCode == 429)
            {
                return new ProviderSnapshot(
                    id, name, accent, planLabel,
                    ProviderStatus.Error,
                    [],
                    "请求频率受限，稍后自动重试",
                    DateTimeOffset.Now);
            }

            if (!resp.IsSuccessStatusCode)
            {
                return new ProviderSnapshot(
                    id, name, accent, planLabel,
                    ProviderStatus.Error,
                    [],
                    $"HTTP {(int)resp.StatusCode}",
                    DateTimeOffset.Now);
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            var rings = QuotaParsers.ParseClaudeJson(body);
            return new ProviderSnapshot(
                id, name, accent, planLabel,
                ProviderStatus.Ready,
                rings,
                null,
                DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            return new ProviderSnapshot(
                id, name, accent, null,
                ProviderStatus.Error,
                [],
                TruncateError(ex.Message),
                DateTimeOffset.Now);
        }
    }

    private string? FindAgyBinary()
    {
        var localExe = Path.Combine(_localAppData, "agy", "bin", "agy.exe");
        if (File.Exists(localExe))
        {
            return localExe;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim();
            foreach (var name in new[] { "agy.exe", "agy.cmd" })
            {
                var candidate = Path.Combine(trimmed, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string TruncateError(string msg)
    {
        var oneLine = msg.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 48 ? oneLine : oneLine[..45] + "...";
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
