using Synthtax.Domain.Enums;

namespace Synthtax.Domain.ValueObjects;

/// <summary>
/// Definierar gränsvärden för en <see cref="SubscriptionPlan"/>.
/// Immutabelt value object — inga instanser lagras i databasen.
///
/// <para><b>Gränsvärden per plan:</b>
/// <list type="table">
///   <listheader><term>Plan</term><term>Licenser</term><term>Projekt</term><term>Skanningar/dag</term><term>Max TierLevel</term></listheader>
///   <item><term>Free</term>        <term>1</term>   <term>2</term>   <term>5</term>     <term>Tier3</term></item>
///   <item><term>Starter</term>     <term>5</term>   <term>10</term>  <term>50</term>    <term>Tier2</term></item>
///   <item><term>Professional</term><term>25</term>  <term>50</term>  <term>500</term>   <term>Tier1</term></item>
///   <item><term>Enterprise</term>  <term>∞</term>   <term>∞</term>   <term>∞</term>     <term>Tier1</term></item>
/// </list>
/// </para>
/// </summary>
public sealed record LicenseLimits
{
    // ── Gränsvärden ────────────────────────────────────────────────────────

    /// <summary>Max antal licenser (aktiva användare) i organisationen. int.MaxValue = obegränsat.</summary>
    public int MaxLicenses { get; init; }

    /// <summary>Max antal projekt. int.MaxValue = obegränsat.</summary>
    public int MaxProjects { get; init; }

    /// <summary>Max antal analyskörningar per dag (rullande 24h-fönster). int.MaxValue = obegränsat.</summary>
    public int MaxScansPerDay { get; init; }

    /// <summary>
    /// Lägsta tillåtna TierLevel för ett nytt projekt.
    /// Tier1 = affärskritisk (kräver Professional eller Enterprise).
    /// </summary>
    public TierLevel MaxAllowedProjectTier { get; init; }

    /// <summary>Tillåter fuzzy-matching (Fas 4). Kräver Starter+.</summary>
    public bool FuzzyMatchingEnabled { get; init; }

    /// <summary>Tillåter CI/CD-integrering. Kräver Professional+.</summary>
    public bool CiCdIntegrationEnabled { get; init; }

    /// <summary>Tillåter SSO/SAML. Kräver Enterprise.</summary>
    public bool SsoEnabled { get; init; }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    public bool IsUnlimitedLicenses => MaxLicenses == int.MaxValue;
    public bool IsUnlimitedProjects => MaxProjects == int.MaxValue;
    public bool IsUnlimitedScans   => MaxScansPerDay == int.MaxValue;

    public bool CanAddLicense(int currentCount)  => IsUnlimitedLicenses || currentCount < MaxLicenses;
    public bool CanAddProject(int currentCount)  => IsUnlimitedProjects || currentCount < MaxProjects;
    public bool CanScan(int todayCount)          => IsUnlimitedScans    || todayCount < MaxScansPerDay;
    public bool AllowsTier(TierLevel tier)       => (int)tier >= (int)MaxAllowedProjectTier;

    // ═══════════════════════════════════════════════════════════════════════
    // Fördefinierade planer
    // ═══════════════════════════════════════════════════════════════════════

    public static LicenseLimits For(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Free         => Free,
        SubscriptionPlan.Starter      => Starter,
        SubscriptionPlan.Professional => Professional,
        SubscriptionPlan.Enterprise   => Enterprise,
        _                             => Free
    };

    public static readonly LicenseLimits Free = new()
    {
        MaxLicenses              = 1,
        MaxProjects              = 2,
        MaxScansPerDay           = 5,
        MaxAllowedProjectTier    = TierLevel.Tier3,
        FuzzyMatchingEnabled     = false,
        CiCdIntegrationEnabled   = false,
        SsoEnabled               = false
    };

    public static readonly LicenseLimits Starter = new()
    {
        MaxLicenses              = 5,
        MaxProjects              = 10,
        MaxScansPerDay           = 50,
        MaxAllowedProjectTier    = TierLevel.Tier2,
        FuzzyMatchingEnabled     = true,
        CiCdIntegrationEnabled   = false,
        SsoEnabled               = false
    };

    public static readonly LicenseLimits Professional = new()
    {
        MaxLicenses              = 25,
        MaxProjects              = 50,
        MaxScansPerDay           = 500,
        MaxAllowedProjectTier    = TierLevel.Tier1,
        FuzzyMatchingEnabled     = true,
        CiCdIntegrationEnabled   = true,
        SsoEnabled               = false
    };

    public static readonly LicenseLimits Enterprise = new()
    {
        MaxLicenses              = int.MaxValue,
        MaxProjects              = int.MaxValue,
        MaxScansPerDay           = int.MaxValue,
        MaxAllowedProjectTier    = TierLevel.Tier1,
        FuzzyMatchingEnabled     = true,
        CiCdIntegrationEnabled   = true,
        SsoEnabled               = true
    };
}
