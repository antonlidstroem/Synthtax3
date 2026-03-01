using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Core.SaaS;
using Synthtax.Domain.Enums;
using Synthtax.Infrastructure.Services;

namespace Synthtax.API.SaaS.Filters;

// ═══════════════════════════════════════════════════════════════════════════
// RequireLicenseAttribute
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Action-filter som kontrollerar licensgränser innan en endpoint exekveras.
///
/// <para><b>Användning:</b>
/// <code>
///   [RequireLicense(LicenseFeature.CiCdIntegration)]
///   [HttpPost("scan")]
///   public async Task&lt;IActionResult&gt; TriggerScan(...)
///
///   [RequireLicense(checkScanQuota: true)]
///   [HttpPost("analyze")]
///   public async Task&lt;IActionResult&gt; Analyze(...)
/// </code>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequireLicenseAttribute : Attribute, IFilterFactory
{
    public LicenseFeature? Feature       { get; }
    public bool            CheckScanQuota { get; }
    public TierLevel?      RequiredTier   { get; }

    public RequireLicenseAttribute(
        LicenseFeature feature)
    {
        Feature = feature;
    }

    public RequireLicenseAttribute(
        bool checkScanQuota = false,
        TierLevel? requiredTier = null)
    {
        CheckScanQuota = checkScanQuota;
        RequiredTier   = requiredTier;
    }

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new LicenseGuardActionFilter(
            serviceProvider.GetRequiredService<ILicenseGuard>(),
            serviceProvider.GetRequiredService<ICurrentUserService>(),
            Feature,
            CheckScanQuota,
            RequiredTier);
}

// ═══════════════════════════════════════════════════════════════════════════
// LicenseGuardActionFilter
// ═══════════════════════════════════════════════════════════════════════════

internal sealed class LicenseGuardActionFilter : IAsyncActionFilter
{
    private readonly ILicenseGuard       _guard;
    private readonly ICurrentUserService _currentUser;
    private readonly LicenseFeature?     _feature;
    private readonly bool                _checkScanQuota;
    private readonly TierLevel?          _requiredTier;

    public LicenseGuardActionFilter(
        ILicenseGuard       guard,
        ICurrentUserService currentUser,
        LicenseFeature?     feature,
        bool                checkScanQuota,
        TierLevel?          requiredTier)
    {
        _guard          = guard;
        _currentUser    = currentUser;
        _feature        = feature;
        _checkScanQuota = checkScanQuota;
        _requiredTier   = requiredTier;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var orgId = _currentUser.OrganizationId;

        // Systemadmin kringgår alltid licenscheckar
        if (_currentUser.IsSystemAdmin)
        {
            await next();
            return;
        }

        if (orgId is null)
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Ingen aktiv organisation i token."
            });
            return;
        }

        LicenseCheckResult? check = null;

        // ── Feature-check ──────────────────────────────────────────────────
        if (_feature.HasValue)
        {
            check = await _guard.CheckFeatureAllowedAsync(orgId.Value, _feature.Value);
            if (!check.IsAllowed)
            {
                context.Result = LicenseDeniedResult(check);
                return;
            }
        }

        // ── Scan-kvotkontroll ─────────────────────────────────────────────
        if (_checkScanQuota)
        {
            check = await _guard.CheckScanAllowedAsync(orgId.Value);
            if (!check.IsAllowed)
            {
                context.Result = LicenseDeniedResult(check);
                return;
            }
        }

        await next();
    }

    private static ObjectResult LicenseDeniedResult(LicenseCheckResult check) =>
        new(new
        {
            error        = check.DenialReason,
            upgradeHint  = check.UpgradeHint,
            currentCount = check.CurrentCount,
            limitCount   = check.LimitCount
        })
        {
            StatusCode = 402  // Payment Required
        };
}
