

namespace Synthtax.Core.SaaS;

// ═══════════════════════════════════════════════════════════════════════════
// LicenseCheckResult
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Resultatet av en licenskontroll.</summary>
public sealed record LicenseCheckResult
{
    public bool    IsAllowed     { get; init; }
    public string? DenialReason  { get; init; }
    public string? UpgradeHint   { get; init; }

    /// <summary>Aktuell användning (t.ex. 48/50 projekt).</summary>
    public int     CurrentCount  { get; init; }
    public int     LimitCount    { get; init; }

    public static LicenseCheckResult Allow() => new() { IsAllowed = true };

    public static LicenseCheckResult Deny(
        string reason,
        string? upgradeHint = null,
        int currentCount = 0,
        int limitCount   = 0) =>
        new()
        {
            IsAllowed    = false,
            DenialReason = reason,
            UpgradeHint  = upgradeHint,
            CurrentCount = currentCount,
            LimitCount   = limitCount
        };

    public override string ToString() =>
        IsAllowed ? "Allowed" : $"Denied: {DenialReason}";
}

// ═══════════════════════════════════════════════════════════════════════════
// ILicenseGuard
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Kontrollerar om en operation är tillåten baserat på organisationens
/// aktiva prenumerationsplan och nuvarande användning.
///
/// <para>Alla kontroller är asynkrona för att möjliggöra DB-anrop
/// (räkna aktiva projekt, skanningar idag etc.).</para>
/// </summary>
public interface ILicenseGuard
{
    /// <summary>
    /// Kontrollerar om en ny analysskanning är tillåten.
    /// Nekar om dagskvoten är uppnådd.
    /// </summary>
    Task<LicenseCheckResult> CheckScanAllowedAsync(
        Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Kontrollerar om ett nytt projekt kan skapas.
    /// Nekar om projektkvoten är uppnådd eller om TierLevel är för hög för planen.
    /// </summary>
    Task<LicenseCheckResult> CheckProjectCreationAllowedAsync(
        Guid organizationId, TierLevel requestedTier, CancellationToken ct = default);

    /// <summary>
    /// Kontrollerar om en ny användare kan bjudas in.
    /// Nekar om licenskvoten (MaxLicenses) är uppnådd.
    /// </summary>
    Task<LicenseCheckResult> CheckUserInviteAllowedAsync(
        Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Kontrollerar om en specifik feature är tillgänglig för planen.
    /// </summary>
    Task<LicenseCheckResult> CheckFeatureAllowedAsync(
        Guid organizationId, LicenseFeature feature, CancellationToken ct = default);
}

/// <summary>Namngivna features som kan licensgardas.</summary>
public enum LicenseFeature
{
    FuzzyMatching       = 0,
    CiCdIntegration     = 1,
    SsoLogin            = 2,
    ApiAccess           = 3,
    AdvancedReporting   = 4
}
