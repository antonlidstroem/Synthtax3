using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Synthtax.Core.SaaS;
using Synthtax.Domain.Enums;
using Synthtax.Domain.ValueObjects;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure.Services;

/// <summary>
/// Implementering av <see cref="ILicenseGuard"/>.
///
/// <para><b>Caching-strategi:</b>
/// Organisationsdata (plan + licenser) cachas i 5 minuter via IMemoryCache
/// för att undvika upprepade DB-anrop per request. Räkningar (projekt/skanningar)
/// cachas i 60 sekunder — acceptabel konsistens för rullande kvot-kontroller.</para>
///
/// <para><b>IgnoreQueryFilters:</b> Alla queries använder <c>.IgnoreQueryFilters()</c>
/// — LicenseGuard är en systemtjänst som behöver cross-tenant data.</para>
/// </summary>
public sealed class LicenseGuardService : ILicenseGuard
{
    private readonly SynthtaxDbContextV5   _db;
    private readonly IMemoryCache          _cache;
    private readonly ILogger<LicenseGuardService> _logger;

    private static readonly TimeSpan OrgCacheTtl   = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CountCacheTtl = TimeSpan.FromSeconds(60);

    public LicenseGuardService(
        SynthtaxDbContextV5          db,
        IMemoryCache                 cache,
        ILogger<LicenseGuardService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ILicenseGuard
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<LicenseCheckResult> CheckScanAllowedAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        var (plan, _) = await GetOrgPlanAsync(organizationId, ct);
        var limits    = LicenseLimits.For(plan);

        if (limits.IsUnlimitedScans) return LicenseCheckResult.Allow();

        var todayScans = await GetScanCountTodayAsync(organizationId, ct);

        if (!limits.CanScan(todayScans))
        {
            _logger.LogWarning(
                "LicenseGuard DENY scan — OrgId:{OrgId} Plan:{Plan} " +
                "TodayScans:{Today}/{Max}",
                organizationId, plan, todayScans, limits.MaxScansPerDay);

            return LicenseCheckResult.Deny(
                reason:      $"Dagskvoten för skanningar är uppnådd ({todayScans}/{limits.MaxScansPerDay}).",
                upgradeHint: UpgradeHintFor(plan),
                currentCount: todayScans,
                limitCount:   limits.MaxScansPerDay);
        }

        return LicenseCheckResult.Allow();
    }

    public async Task<LicenseCheckResult> CheckProjectCreationAllowedAsync(
        Guid organizationId, TierLevel requestedTier, CancellationToken ct = default)
    {
        var (plan, _) = await GetOrgPlanAsync(organizationId, ct);
        var limits    = LicenseLimits.For(plan);

        // Kontroll 1: Tier-nivå
        if (!limits.AllowsTier(requestedTier))
        {
            _logger.LogWarning(
                "LicenseGuard DENY project-tier — OrgId:{OrgId} Plan:{Plan} " +
                "RequestedTier:{Tier} MaxAllowed:{Max}",
                organizationId, plan, requestedTier, limits.MaxAllowedProjectTier);

            return LicenseCheckResult.Deny(
                reason:      $"Planen '{plan}' tillåter inte projekt med TierLevel '{requestedTier}'. " +
                             $"Max tillåtet är '{limits.MaxAllowedProjectTier}'.",
                upgradeHint: UpgradeHintFor(plan));
        }

        // Kontroll 2: Projektkvot
        if (!limits.IsUnlimitedProjects)
        {
            var projectCount = await GetActiveProjectCountAsync(organizationId, ct);
            if (!limits.CanAddProject(projectCount))
            {
                _logger.LogWarning(
                    "LicenseGuard DENY project-quota — OrgId:{OrgId} Plan:{Plan} " +
                    "Projects:{Count}/{Max}",
                    organizationId, plan, projectCount, limits.MaxProjects);

                return LicenseCheckResult.Deny(
                    reason:      $"Projektkvoten är uppnådd ({projectCount}/{limits.MaxProjects}).",
                    upgradeHint: UpgradeHintFor(plan),
                    currentCount: projectCount,
                    limitCount:   limits.MaxProjects);
            }
        }

        return LicenseCheckResult.Allow();
    }

    public async Task<LicenseCheckResult> CheckUserInviteAllowedAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        var (plan, purchasedLicenses) = await GetOrgPlanAsync(organizationId, ct);
        var limits = LicenseLimits.For(plan);

        // Effektiv gräns = lägst av planens max och köpta licenser
        var effectiveMax = Math.Min(
            limits.IsUnlimitedLicenses ? int.MaxValue : limits.MaxLicenses,
            purchasedLicenses);

        if (effectiveMax == int.MaxValue) return LicenseCheckResult.Allow();

        var activeMembers = await GetActiveMemberCountAsync(organizationId, ct);

        if (activeMembers >= effectiveMax)
        {
            _logger.LogWarning(
                "LicenseGuard DENY invite — OrgId:{OrgId} Plan:{Plan} " +
                "Members:{Count}/{Max} PurchasedLicenses:{PL}",
                organizationId, plan, activeMembers, effectiveMax, purchasedLicenses);

            return LicenseCheckResult.Deny(
                reason:      $"Alla {effectiveMax} licenser är använda ({activeMembers} aktiva membres).",
                upgradeHint: $"Köp fler licenser eller uppgradera till {NextPlan(plan)}.",
                currentCount: activeMembers,
                limitCount:   effectiveMax);
        }

        return LicenseCheckResult.Allow();
    }

    public async Task<LicenseCheckResult> CheckFeatureAllowedAsync(
        Guid organizationId, LicenseFeature feature, CancellationToken ct = default)
    {
        var (plan, _) = await GetOrgPlanAsync(organizationId, ct);
        var limits    = LicenseLimits.For(plan);

        var isAllowed = feature switch
        {
            LicenseFeature.FuzzyMatching     => limits.FuzzyMatchingEnabled,
            LicenseFeature.CiCdIntegration   => limits.CiCdIntegrationEnabled,
            LicenseFeature.SsoLogin          => limits.SsoEnabled,
            LicenseFeature.ApiAccess         => plan >= SubscriptionPlan.Starter,
            LicenseFeature.AdvancedReporting => plan >= SubscriptionPlan.Professional,
            _                                => false
        };

        if (!isAllowed)
        {
            _logger.LogInformation(
                "LicenseGuard DENY feature — OrgId:{OrgId} Plan:{Plan} Feature:{Feature}",
                organizationId, plan, feature);

            return LicenseCheckResult.Deny(
                reason:      $"Funktionen '{feature}' ingår inte i planen '{plan}'.",
                upgradeHint: UpgradeHintFor(plan));
        }

        return LicenseCheckResult.Allow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Privata hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<(SubscriptionPlan Plan, int PurchasedLicenses)> GetOrgPlanAsync(
        Guid organizationId, CancellationToken ct)
    {
        var cacheKey = $"org-plan:{organizationId:N}";
        if (_cache.TryGetValue(cacheKey, out (SubscriptionPlan, int) cached))
            return cached;

        var org = await _db.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.Id == organizationId && o.IsActive)
            .Select(o => new { o.Plan, o.PurchasedLicenses })
            .FirstOrDefaultAsync(ct);

        if (org is null)
        {
            _logger.LogWarning("LicenseGuard: organisation {OrgId} hittades inte.", organizationId);
            return (SubscriptionPlan.Free, 1); // säkert fallback
        }

        var result = (org.Plan, org.PurchasedLicenses);
        _cache.Set(cacheKey, result, OrgCacheTtl);
        return result;
    }

    private async Task<int> GetScanCountTodayAsync(Guid organizationId, CancellationToken ct)
    {
        var cacheKey = $"scans-today:{organizationId:N}:{DateTime.UtcNow:yyyyMMdd}";
        if (_cache.TryGetValue(cacheKey, out int cachedCount)) return cachedCount;

        var since = DateTime.UtcNow.Date; // midnatt UTC
        var count = await _db.AnalysisSessions
            .IgnoreQueryFilters()
            .Where(s => s.Project.TenantId == organizationId && s.Timestamp >= since)
            .CountAsync(ct);

        _cache.Set(cacheKey, count, CountCacheTtl);
        return count;
    }

    private async Task<int> GetActiveProjectCountAsync(Guid organizationId, CancellationToken ct)
    {
        var cacheKey = $"project-count:{organizationId:N}";
        if (_cache.TryGetValue(cacheKey, out int cachedCount)) return cachedCount;

        var count = await _db.Projects
            .IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == organizationId && !p.IsDeleted, ct);

        _cache.Set(cacheKey, count, CountCacheTtl);
        return count;
    }

    private async Task<int> GetActiveMemberCountAsync(Guid organizationId, CancellationToken ct)
    {
        var cacheKey = $"member-count:{organizationId:N}";
        if (_cache.TryGetValue(cacheKey, out int cachedCount)) return cachedCount;

        var count = await _db.OrganizationMemberships
            .IgnoreQueryFilters()
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);

        _cache.Set(cacheKey, count, CountCacheTtl);
        return count;
    }

    private static string UpgradeHintFor(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Free         => "Uppgradera till Starter för fler licenser och projekt.",
        SubscriptionPlan.Starter      => "Uppgradera till Professional för obegränsat Tier1-stöd och CI/CD.",
        SubscriptionPlan.Professional => "Kontakta sales för Enterprise-plan med obegränsade resurser.",
        _                             => "Kontakta supporten."
    };

    private static string NextPlan(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Free         => "Starter",
        SubscriptionPlan.Starter      => "Professional",
        SubscriptionPlan.Professional => "Enterprise",
        _                             => "Enterprise"
    };
}
