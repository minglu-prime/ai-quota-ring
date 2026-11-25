namespace QuotaRing.Models;

public enum ProviderStatus
{
    Loading,
    Ready,
    NotSignedIn,
    Error
}

public sealed record QuotaRingItem(
    string Id,
    string Label,
    string? SubLabel,
    double RemainingPercent,
    DateTimeOffset? ResetsAt)
{
    public double ClampedRemaining => Math.Clamp(RemainingPercent, 0.0, 100.0);
    public double UsedPercent => Math.Clamp(100.0 - ClampedRemaining, 0.0, 100.0);
}

public sealed record ProviderSnapshot(
    string ProviderId,
    string DisplayName,
    string AccentHex,
    string? PlanLabel,
    ProviderStatus Status,
    IReadOnlyList<QuotaRingItem> Rings,
    string? StatusMessage,
    DateTimeOffset FetchedAt);

public sealed class AppSettings
{
    public bool DarkTheme { get; set; } = true;
    public bool Topmost { get; set; } = true;
    public bool Locked { get; set; } = false;
    public double? Left { get; set; }
    public double? Top { get; set; }
}
