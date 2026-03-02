// Synthtax.Tests/SaaS/SaasTests.cs
// Requires: xunit, FluentAssertions, NSubstitute, Microsoft.EntityFrameworkCore.InMemory

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;
using Synthtax.API.SaaS.JWT;
using Synthtax.Application.SaaS;
using Synthtax.Core.SaaS;
using Synthtax.Domain.Entities;
using Synthtax.Domain.Enums;
using Synthtax.Domain.ValueObjects;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Services;

namespace Synthtax.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// LicenseLimits — värdeobject
// ═══════════════════════════════════════════════════════════════════════════

public class LicenseLimitsTests
{
    // ── For() returnerar rätt plan ─────────────────────────────────────────

    [Theory]
    [InlineData(SubscriptionPlan.Free,         1,           2,  5)]
    [InlineData(SubscriptionPlan.Starter,      5,          10, 50)]
    [InlineData(SubscriptionPlan.Professional, 25,         50, 500)]
    public void For_ReturnsCorrectLimits(
        SubscriptionPlan plan, int maxLic, int maxProj, int maxScans)
    {
        var limits = LicenseLimits.For(plan);
        limits.MaxLicenses.Should().Be(maxLic);
        limits.MaxProjects.Should().Be(maxProj);
        limits.MaxScansPerDay.Should().Be(maxScans);
    }

    [Fact]
    public void For_Enterprise_IsUnlimited()
    {
        var limits = LicenseLimits.For(SubscriptionPlan.Enterprise);
        limits.IsUnlimitedLicenses.Should().BeTrue();
        limits.IsUnlimitedProjects.Should().BeTrue();
        limits.IsUnlimitedScans.Should().BeTrue();
    }

    // ── CanAddProject ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(SubscriptionPlan.Free, 1, true)]   // 1 av 2 → ok
    [InlineData(SubscriptionPlan.Free, 2, false)]  // 2 av 2 → full
    [InlineData(SubscriptionPlan.Free, 3, false)]  // överskrider → nekad
    public void CanAddProject_RespectsLimit(
        SubscriptionPlan plan, int currentCount, bool expected)
    {
        LicenseLimits.For(plan).CanAddProject(currentCount).Should().Be(expected);
    }

    // ── CanScan ────────────────────────────────────────────────────────────

    [Fact]
    public void CanScan_AtLimit_ReturnsFalse()
    {
        var limits = LicenseLimits.For(SubscriptionPlan.Free); // MaxScansPerDay = 5
        limits.CanScan(5).Should().BeFalse();
        limits.CanScan(4).Should().BeTrue();
    }

    // ── AllowsTier ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SubscriptionPlan.Free,         TierLevel.Tier4, true)]
    [InlineData(SubscriptionPlan.Free,         TierLevel.Tier3, true)]   // MaxAllowed = Tier3
    [InlineData(SubscriptionPlan.Free,         TierLevel.Tier2, false)]  // Tier2 kräver Starter+
    [InlineData(SubscriptionPlan.Free,         TierLevel.Tier1, false)]
    [InlineData(SubscriptionPlan.Starter,      TierLevel.Tier2, true)]
    [InlineData(SubscriptionPlan.Starter,      TierLevel.Tier1, false)]
    [InlineData(SubscriptionPlan.Professional, TierLevel.Tier1, true)]
    [InlineData(SubscriptionPlan.Enterprise,   TierLevel.Tier1, true)]
    public void AllowsTier_CorrectPerPlan(
        SubscriptionPlan plan, TierLevel tier, bool expected)
    {
        LicenseLimits.For(plan).AllowsTier(tier).Should().Be(expected);
    }

    // ── Features ──────────────────────────────────────────────────────────

    [Fact]
    public void Free_DisablesFuzzyMatchingAndCiCd()
    {
        var limits = LicenseLimits.Free;
        limits.FuzzyMatchingEnabled.Should().BeFalse();
        limits.CiCdIntegrationEnabled.Should().BeFalse();
        limits.SsoEnabled.Should().BeFalse();
    }

    [Fact]
    public void Professional_EnablesCiCdButNotSso()
    {
        var limits = LicenseLimits.Professional;
        limits.FuzzyMatchingEnabled.Should().BeTrue();
        limits.CiCdIntegrationEnabled.Should().BeTrue();
        limits.SsoEnabled.Should().BeFalse();
    }

    [Fact]
    public void Enterprise_EnablesAll()
    {
        var limits = LicenseLimits.Enterprise;
        limits.FuzzyMatchingEnabled.Should().BeTrue();
        limits.CiCdIntegrationEnabled.Should().BeTrue();
        limits.SsoEnabled.Should().BeTrue();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// LicenseGuardService — integrationstest med InMemory DB
// ═══════════════════════════════════════════════════════════════════════════

public class LicenseGuardServiceTests : IDisposable
{
    private readonly SynthtaxDbContextV5 _db;
    private readonly ILicenseGuard        _sut;
    private readonly IMemoryCache         _cache;

    private static readonly Guid OrgId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public LicenseGuardServiceTests()
    {
        var options = new DbContextOptionsBuilder<SynthtaxDbContextV5>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db    = new SynthtaxDbContextV5(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _sut   = new LicenseGuardService(_db, _cache, NullLogger<LicenseGuardService>.Instance);
    }

    public void Dispose() { _db.Dispose(); _cache.Dispose(); }

    // ── CheckScanAllowed ──────────────────────────────────────────────────

    [Fact]
    public async Task CheckScanAllowed_BelowQuota_Allows()
    {
        await SeedOrgAsync(SubscriptionPlan.Starter); // MaxScansPerDay = 50
        await SeedScanSessionsAsync(count: 10);

        var result = await _sut.CheckScanAllowedAsync(OrgId);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckScanAllowed_AtQuota_Denies()
    {
        await SeedOrgAsync(SubscriptionPlan.Free); // MaxScansPerDay = 5
        await SeedScanSessionsAsync(count: 5);

        var result = await _sut.CheckScanAllowedAsync(OrgId);
        result.IsAllowed.Should().BeFalse();
        result.DenialReason.Should().Contain("5/5");
        result.UpgradeHint.Should().NotBeNullOrEmpty();
        result.CurrentCount.Should().Be(5);
        result.LimitCount.Should().Be(5);
    }

    [Fact]
    public async Task CheckScanAllowed_Enterprise_AlwaysAllows()
    {
        await SeedOrgAsync(SubscriptionPlan.Enterprise);
        await SeedScanSessionsAsync(count: 999_999); // artificiellt högt

        var result = await _sut.CheckScanAllowedAsync(OrgId);
        result.IsAllowed.Should().BeTrue("Enterprise är obegränsat");
    }

    // ── CheckProjectCreationAllowed ───────────────────────────────────────

    [Fact]
    public async Task CheckProjectCreation_TierTooHigh_Denies()
    {
        await SeedOrgAsync(SubscriptionPlan.Free); // MaxAllowed = Tier3

        var result = await _sut.CheckProjectCreationAllowedAsync(OrgId, TierLevel.Tier1);
        result.IsAllowed.Should().BeFalse();
        result.DenialReason.Should().Contain("Tier1");
    }

    [Fact]
    public async Task CheckProjectCreation_AtProjectQuota_Denies()
    {
        await SeedOrgAsync(SubscriptionPlan.Free); // MaxProjects = 2
        await SeedProjectsAsync(count: 2);

        var result = await _sut.CheckProjectCreationAllowedAsync(OrgId, TierLevel.Tier3);
        result.IsAllowed.Should().BeFalse();
        result.CurrentCount.Should().Be(2);
        result.LimitCount.Should().Be(2);
    }

    [Fact]
    public async Task CheckProjectCreation_BelowQuota_ValidTier_Allows()
    {
        await SeedOrgAsync(SubscriptionPlan.Starter); // MaxProjects=10, MaxTier=Tier2
        await SeedProjectsAsync(count: 3);

        var result = await _sut.CheckProjectCreationAllowedAsync(OrgId, TierLevel.Tier2);
        result.IsAllowed.Should().BeTrue();
    }

    // ── CheckUserInviteAllowed ────────────────────────────────────────────

    [Fact]
    public async Task CheckUserInvite_BelowPurchasedLicenses_Allows()
    {
        await SeedOrgAsync(SubscriptionPlan.Starter, purchasedLicenses: 5);
        await SeedMembersAsync(count: 3);

        var result = await _sut.CheckUserInviteAllowedAsync(OrgId);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckUserInvite_AtPurchasedLicenses_Denies()
    {
        await SeedOrgAsync(SubscriptionPlan.Starter, purchasedLicenses: 3);
        await SeedMembersAsync(count: 3);

        var result = await _sut.CheckUserInviteAllowedAsync(OrgId);
        result.IsAllowed.Should().BeFalse();
        result.CurrentCount.Should().Be(3);
        result.LimitCount.Should().Be(3);
    }

    [Fact]
    public async Task CheckUserInvite_PlanMaxLicensesLowerThanPurchased_UsesMin()
    {
        // Free-plan: MaxLicenses=1, men köpta=5 → effektiv gräns = 1 (planen vinner)
        await SeedOrgAsync(SubscriptionPlan.Free, purchasedLicenses: 5);
        await SeedMembersAsync(count: 1);

        var result = await _sut.CheckUserInviteAllowedAsync(OrgId);
        result.IsAllowed.Should().BeFalse("Free har bara 1 licens oavsett köpta");
        result.LimitCount.Should().Be(1);
    }

    // ── CheckFeatureAllowed ───────────────────────────────────────────────

    [Theory]
    [InlineData(SubscriptionPlan.Free,         LicenseFeature.FuzzyMatching,   false)]
    [InlineData(SubscriptionPlan.Starter,      LicenseFeature.FuzzyMatching,   true)]
    [InlineData(SubscriptionPlan.Starter,      LicenseFeature.CiCdIntegration, false)]
    [InlineData(SubscriptionPlan.Professional, LicenseFeature.CiCdIntegration, true)]
    [InlineData(SubscriptionPlan.Professional, LicenseFeature.SsoLogin,        false)]
    [InlineData(SubscriptionPlan.Enterprise,   LicenseFeature.SsoLogin,        true)]
    public async Task CheckFeatureAllowed_CorrectPerPlanAndFeature(
        SubscriptionPlan plan, LicenseFeature feature, bool expected)
    {
        await SeedOrgAsync(plan);
        var result = await _sut.CheckFeatureAllowedAsync(OrgId, feature);
        result.IsAllowed.Should().Be(expected);
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private async Task SeedOrgAsync(SubscriptionPlan plan, int purchasedLicenses = 1)
    {
        _db.Organizations.Add(new Organization
        {
            Id               = OrgId,
            Name             = "Test Org",
            Slug             = "test-org",
            Plan             = plan,
            PurchasedLicenses = purchasedLicenses,
            IsActive         = true
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedScanSessionsAsync(int count)
    {
        var project = new Project
        {
            Id       = Guid.NewGuid(),
            Name     = "P",
            TenantId = OrgId
        };
        _db.Projects.Add(project);

        for (int i = 0; i < count; i++)
        {
            _db.AnalysisSessions.Add(new AnalysisSession
            {
                Id        = Guid.NewGuid(),
                ProjectId = project.Id,
                Timestamp = DateTime.UtcNow // idag
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedProjectsAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _db.Projects.Add(new Project
            {
                Id       = Guid.NewGuid(),
                Name     = $"Projekt {i}",
                TenantId = OrgId
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedMembersAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _db.OrganizationMemberships.Add(new OrganizationMembership
            {
                Id             = Guid.NewGuid(),
                OrganizationId = OrgId,
                UserId         = Guid.NewGuid().ToString(),
                Role           = OrgRole.Member,
                IsActive       = true
            });
        }
        await _db.SaveChangesAsync();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// OrgClaimsExtensions — JWT claim-läsning
// ═══════════════════════════════════════════════════════════════════════════

public class OrgClaimsExtensionsTests
{
    private static ClaimsPrincipal MakePrincipal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            "Test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void GetOrganizationId_ValidGuid_ReturnsGuid()
    {
        var orgId     = Guid.NewGuid();
        var principal = MakePrincipal((SynthtaxClaimTypes.OrganizationId, orgId.ToString("D")));

        principal.GetOrganizationId().Should().Be(orgId);
    }

    [Fact]
    public void GetOrganizationId_MissingClaim_ReturnsNull()
    {
        var principal = MakePrincipal(); // inga claims
        principal.GetOrganizationId().Should().BeNull();
    }

    [Fact]
    public void GetOrganizationId_InvalidGuid_ReturnsNull()
    {
        var principal = MakePrincipal((SynthtaxClaimTypes.OrganizationId, "not-a-guid"));
        principal.GetOrganizationId().Should().BeNull();
    }

    [Fact]
    public void GetTenantId_WithOrg_ReturnsOrgId()
    {
        var orgId     = Guid.NewGuid();
        var principal = MakePrincipal((SynthtaxClaimTypes.OrganizationId, orgId.ToString("D")));

        principal.GetTenantId().Should().Be(orgId);
    }

    [Fact]
    public void GetTenantId_WithoutOrg_ReturnsGuidEmpty()
    {
        var principal = MakePrincipal();
        principal.GetTenantId().Should().Be(Guid.Empty);
    }

    [Theory]
    [InlineData("Member",   OrgRole.Member)]
    [InlineData("OrgAdmin", OrgRole.OrgAdmin)]
    public void GetOrgRole_ValidRole_ReturnsEnum(string claimValue, OrgRole expected)
    {
        var principal = MakePrincipal((SynthtaxClaimTypes.OrgRole, claimValue));
        principal.GetOrgRole().Should().Be(expected);
    }

    [Fact]
    public void GetOrgRole_MissingClaim_ReturnsNull()
    {
        var principal = MakePrincipal();
        principal.GetOrgRole().Should().BeNull();
    }

    [Fact]
    public void IsSystemAdmin_SysAdminClaim_ReturnsTrue()
    {
        var principal = MakePrincipal((SynthtaxClaimTypes.IsSystemAdmin, "true"));
        principal.IsSystemAdmin().Should().BeTrue();
    }

    [Fact]
    public void IsSystemAdmin_AdminRole_ReturnsTrue()
    {
        var principal = MakePrincipal((ClaimTypes.Role, "Admin"));
        principal.IsSystemAdmin().Should().BeTrue();
    }

    [Fact]
    public void BuildOrgClaims_ContainsAllRequiredClaims()
    {
        var orgId = Guid.NewGuid();
        var claims = OrgClaimsExtensions.BuildOrgClaims(
            userId:           "user-123",
            userName:         "alice",
            organizationId:   orgId,
            organizationSlug: "acme-corp",
            orgRole:          OrgRole.OrgAdmin,
            plan:             SubscriptionPlan.Professional).ToList();

        claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "user-123");
        claims.Should().Contain(c => c.Type == SynthtaxClaimTypes.OrganizationId   && c.Value == orgId.ToString("D"));
        claims.Should().Contain(c => c.Type == SynthtaxClaimTypes.OrganizationSlug && c.Value == "acme-corp");
        claims.Should().Contain(c => c.Type == SynthtaxClaimTypes.OrgRole          && c.Value == "OrgAdmin");
        claims.Should().Contain(c => c.Type == SynthtaxClaimTypes.SubscriptionPlan && c.Value == "Professional");
    }

    [Fact]
    public void BuildOrgClaims_SystemAdmin_HasSysAdminClaim()
    {
        var claims = OrgClaimsExtensions.BuildOrgClaims(
            "u", "u", Guid.NewGuid(), "slug",
            OrgRole.OrgAdmin, SubscriptionPlan.Enterprise,
            isSystemAdmin: true).ToList();

        claims.Should().Contain(c => c.Type == SynthtaxClaimTypes.IsSystemAdmin && c.Value == "true");
        claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// HttpContextCurrentUserServiceV5 — unit test
// ═══════════════════════════════════════════════════════════════════════════

public class CurrentUserServiceV5Tests
{
    private static ICurrentUserService BuildService(
        string? userId = null,
        Guid? orgId = null,
        OrgRole? role = null,
        bool sysAdmin = false)
    {
        var claims = new List<Claim>();
        if (userId != null)  claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (orgId != null)   claims.Add(new Claim(SynthtaxClaimTypes.OrganizationId, orgId.Value.ToString("D")));
        if (role != null)    claims.Add(new Claim(SynthtaxClaimTypes.OrgRole, role.Value.ToString()));
        if (sysAdmin)        claims.Add(new Claim(SynthtaxClaimTypes.IsSystemAdmin, "true"));

        var identity  = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        var accessor = Substitute.For<IHttpContextAccessor>();
        var httpCtx  = Substitute.For<Microsoft.AspNetCore.Http.HttpContext>();
        httpCtx.User.Returns(principal);
        accessor.HttpContext.Returns(httpCtx);

        return new HttpContextCurrentUserServiceV5(accessor);
    }

    [Fact]
    public void UserId_ReadsNameIdentifierClaim()
    {
        var svc = BuildService(userId: "user-999");
        svc.UserId.Should().Be("user-999");
    }

    [Fact]
    public void OrganizationId_ReadsOrgIdClaim()
    {
        var orgId = Guid.NewGuid();
        var svc   = BuildService(orgId: orgId);
        svc.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public void TenantId_IsOrgIdWhenPresent()
    {
        var orgId = Guid.NewGuid();
        var svc   = BuildService(orgId: orgId);
        svc.TenantId.Should().Be(orgId);
    }

    [Fact]
    public void TenantId_IsGuidEmptyWhenNoOrg()
    {
        var svc = BuildService();
        svc.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void IsOrgAdmin_TrueWhenOrgAdminRole()
    {
        var svc = BuildService(role: OrgRole.OrgAdmin);
        svc.IsOrgAdmin.Should().BeTrue();
    }

    [Fact]
    public void IsOrgAdmin_TrueWhenSystemAdmin()
    {
        var svc = BuildService(sysAdmin: true);
        svc.IsOrgAdmin.Should().BeTrue();
    }

    [Fact]
    public void IsOrgAdmin_FalseForMember()
    {
        var svc = BuildService(role: OrgRole.Member);
        svc.IsOrgAdmin.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// InvitationService — integrationstest
// ═══════════════════════════════════════════════════════════════════════════

public class InvitationServiceTests : IDisposable
{
    private readonly SynthtaxDbContextV5 _db;
    private readonly ILicenseGuard        _guard;
    private readonly IInvitationService   _sut;

    private static readonly Guid OrgId      = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AdminUserId = Guid.NewGuid();

    public InvitationServiceTests()
    {
        var options = new DbContextOptionsBuilder<SynthtaxDbContextV5>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db    = new SynthtaxDbContextV5(options);
        _guard = Substitute.For<ILicenseGuard>();
        _guard.CheckUserInviteAllowedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(LicenseCheckResult.Allow());

        _sut = new InvitationService(_db, _guard, NullLogger<InvitationService>.Instance);

        SeedOrg();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task InviteAsync_ValidEmail_CreatesInvitation()
    {
        var req    = new InviteUserRequest(OrgId, "alice@example.com", OrgRole.Member, AdminUserId.ToString());
        var result = await _sut.InviteAsync(req);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Email.Should().Be("alice@example.com");
        result.Data.Status.Should().Be(InvitationStatus.Pending);
        result.Data.Token.Should().HaveLength(64, "256 bitar hex = 64 tecken");
    }

    [Fact]
    public async Task InviteAsync_InvalidEmail_Fails()
    {
        var req    = new InviteUserRequest(OrgId, "not-an-email", OrgRole.Member, AdminUserId.ToString());
        var result = await _sut.InviteAsync(req);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("e-postadress");
    }

    [Fact]
    public async Task InviteAsync_LicenseDenied_Fails()
    {
        _guard.CheckUserInviteAllowedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(LicenseCheckResult.Deny("Kvoten full.", "Uppgradera."));

        var req    = new InviteUserRequest(OrgId, "bob@example.com", OrgRole.Member, AdminUserId.ToString());
        var result = await _sut.InviteAsync(req);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Kvoten full");
    }

    [Fact]
    public async Task InviteAsync_DuplicateActivePending_Fails()
    {
        var req = new InviteUserRequest(OrgId, "charlie@example.com", OrgRole.Member, AdminUserId.ToString());

        var first = await _sut.InviteAsync(req);
        first.Success.Should().BeTrue();

        var second = await _sut.InviteAsync(req); // samma e-post
        second.Success.Should().BeFalse();
        second.Error.Should().Contain("aktiv inbjudan");
    }

    [Fact]
    public async Task AcceptAsync_ValidToken_CreatesMembership()
    {
        var inviteReq = new InviteUserRequest(OrgId, "diana@example.com", OrgRole.Member, AdminUserId.ToString());
        var invite    = await _sut.InviteAsync(inviteReq);
        invite.Success.Should().BeTrue();

        var acceptReq = new AcceptInvitationRequest(invite.Data!.Token, Guid.NewGuid().ToString());
        var result    = await _sut.AcceptAsync(acceptReq);

        result.Success.Should().BeTrue();
        result.Data!.Role.Should().Be(OrgRole.Member);
        result.Data.IsActive.Should().BeTrue();

        // Inbjudan ska vara markerad som accepterad
        var updatedInvite = await _db.Invitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Token == invite.Data.Token);
        updatedInvite!.Status.Should().Be(InvitationStatus.Accepted);
    }

    [Fact]
    public async Task AcceptAsync_InvalidToken_Fails()
    {
        var req    = new AcceptInvitationRequest("ogiltig-token", Guid.NewGuid().ToString());
        var result = await _sut.AcceptAsync(req);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Ogiltig");
    }

    [Fact]
    public async Task RevokeAsync_PendingInvitation_Revokes()
    {
        var inviteReq = new InviteUserRequest(OrgId, "eve@example.com", OrgRole.Member, AdminUserId.ToString());
        var invite    = await _sut.InviteAsync(inviteReq);

        var result = await _sut.RevokeAsync(invite.Data!.Id, AdminUserId.ToString());
        result.Success.Should().BeTrue();

        var updated = await _db.Invitations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == invite.Data.Id);
        updated!.Status.Should().Be(InvitationStatus.Revoked);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsOnlyPendingForOrg()
    {
        await _sut.InviteAsync(new InviteUserRequest(OrgId, "f1@x.com", OrgRole.Member, AdminUserId.ToString()));
        await _sut.InviteAsync(new InviteUserRequest(OrgId, "f2@x.com", OrgRole.Member, AdminUserId.ToString()));

        var pending = await _sut.GetPendingAsync(OrgId);
        pending.Should().HaveCount(2);
        pending.Should().AllSatisfy(i => i.Status.Should().Be(InvitationStatus.Pending));
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private void SeedOrg()
    {
        _db.Organizations.Add(new Organization
        {
            Id               = OrgId,
            Name             = "Test Org",
            Slug             = "test-org",
            Plan             = SubscriptionPlan.Starter,
            PurchasedLicenses = 5,
            IsActive         = true
        });
        _db.SaveChanges();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Tenant Global Query Filter — röktest
// ═══════════════════════════════════════════════════════════════════════════

public class TenantQueryFilterTests : IDisposable
{
    private readonly SynthtaxDbContextV5 _db;

    private static readonly Guid OrgA = new("aaaa0000-0000-0000-0000-000000000000");
    private static readonly Guid OrgB = new("bbbb0000-0000-0000-0000-000000000000");

    public TenantQueryFilterTests()
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.TenantId.Returns(OrgA);
        currentUser.OrganizationId.Returns(OrgA);
        currentUser.IsSystemAdmin.Returns(false);

        var options = new DbContextOptionsBuilder<SynthtaxDbContextV5>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new SynthtaxDbContextV5(options, currentUser);
        SeedProjects();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Query_ReturnsOnlyOwnTenantProjects()
    {
        // _db filtrerar via Global Query Filter på TenantId = OrgA
        var projects = _db.Projects.ToList();
        projects.Should().HaveCount(1, "bara OrgA:s projekt är synligt");
        projects[0].TenantId.Should().Be(OrgA);
    }

    [Fact]
    public void Query_IgnoreQueryFilters_ReturnsBothTenants()
    {
        var all = _db.Projects.IgnoreQueryFilters().ToList();
        all.Should().HaveCount(2, "IgnoreQueryFilters ger cross-tenant access");
    }

    private void SeedProjects()
    {
        _db.Projects.AddRange(
            new Project { Id = Guid.NewGuid(), Name = "OrgA-projekt", TenantId = OrgA },
            new Project { Id = Guid.NewGuid(), Name = "OrgB-projekt", TenantId = OrgB });
        _db.SaveChanges();
    }
}
