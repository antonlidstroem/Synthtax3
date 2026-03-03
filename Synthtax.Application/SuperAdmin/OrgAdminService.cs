using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Core.Entities;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Application.SuperAdmin;

// ═══════════════════════════════════════════════════════════════════════════
// Kontrakt
// ═══════════════════════════════════════════════════════════════════════════

public interface IOrgAdminService
{
    Task<OrgListResponse>   ListOrgsAsync(int page, int pageSize, string? search,
                                          string? planFilter, bool? activeOnly,
                                          CancellationToken ct = default);
    Task<OrgAdminDto>       GetOrgAsync(Guid id, CancellationToken ct = default);
    Task<OrgAdminDto>       CreateOrgAsync(CreateOrgRequest request, CancellationToken ct = default);
    Task<OrgAdminDto>       UpdateOrgAsync(Guid id, UpdateOrgRequest request, CancellationToken ct = default);
    Task                    DeactivateOrgAsync(Guid id, string reason, CancellationToken ct = default);
    Task                    ReactivateOrgAsync(Guid id, CancellationToken ct = default);
    Task<int>               GetOrgCountAsync(CancellationToken ct = default);
}

// ═══════════════════════════════════════════════════════════════════════════
// Implementation
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// SuperAdmin-service för organisations-hantering.
///
/// <para>Kringgår tenant-filtret via <c>IgnoreQueryFilters()</c> —
/// super-admin ser alla organisationer. Kräver att anroparen har
/// <c>IsSystemAdmin = true</c> (verifieras av [RequireSystemAdmin]-attributet
/// på controller-nivå).</para>
/// </summary>
public sealed class OrgAdminService : IOrgAdminService
{
    private readonly SynthtaxDbContext _db;
    private readonly ILogger<OrgAdminService> _logger;

    public OrgAdminService(SynthtaxDbContext db, ILogger<OrgAdminService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Hämta ─────────────────────────────────────────────────────────────

    public async Task<OrgListResponse> ListOrgsAsync(
        int page, int pageSize, string? search, string? planFilter, bool? activeOnly,
        CancellationToken ct = default)
    {
        var query = _db.Organizations
            .IgnoreQueryFilters()  // Kringgår tenant-filter (Fas 5)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o =>
                o.Name.Contains(search) || o.Slug.Contains(search) ||
                (o.BillingEmail != null && o.BillingEmail.Contains(search)));

        if (!string.IsNullOrWhiteSpace(planFilter) &&
            Enum.TryParse<SubscriptionPlan>(planFilter, ignoreCase: true, out var plan))
            query = query.Where(o => o.Plan == plan);

        if (activeOnly == true)  query = query.Where(o => o.IsActive);
        if (activeOnly == false) query = query.Where(o => !o.IsActive);

        var total = await query.CountAsync(ct);

        var orgs = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Batch-hämta statistik per org
        var orgIds = orgs.Select(o => o.Id).ToList();
        var stats  = await BuildOrgStatsAsync(orgIds, ct);

        var items = orgs.Select(o => MapToDto(o, stats.GetValueOrDefault(o.Id))).ToList();

        return new OrgListResponse
        {
            Items      = items,
            TotalCount = total,
            Page       = page,
            PageSize   = pageSize
        };
    }

    public async Task<OrgAdminDto> GetOrgAsync(Guid id, CancellationToken ct = default)
    {
        var org = await _db.Organizations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException($"Organization {id} not found.");

        var stats = await BuildOrgStatsAsync([id], ct);
        return MapToDto(org, stats.GetValueOrDefault(id));
    }

    // ── Skapa ─────────────────────────────────────────────────────────────

    public async Task<OrgAdminDto> CreateOrgAsync(
        CreateOrgRequest req, CancellationToken ct = default)
    {
        // Slug-validering
        var slugExists = await _db.Organizations
            .IgnoreQueryFilters()
            .AnyAsync(o => o.Slug == req.Slug, ct);
        if (slugExists)
            throw new InvalidOperationException($"Slug '{req.Slug}' is already taken.");

        if (!Enum.TryParse<SubscriptionPlan>(req.Plan, ignoreCase: true, out var plan))
            throw new ArgumentException($"Unknown plan '{req.Plan}'.");

        var org = new Organization
        {
            Id                = Guid.NewGuid(),
            Name              = req.Name.Trim(),
            Slug              = req.Slug.ToLowerInvariant().Trim(),
            Plan              = plan,
            PurchasedLicenses = req.PurchasedLicenses,
            BillingEmail      = req.BillingEmail?.Trim(),
            IsActive          = true,
            TrialEndsAt       = req.StartOnTrial
                                    ? DateTime.UtcNow.AddDays(14)
                                    : null,
            FeaturesJson      = SerializeFeatures(req.EnabledFeatures)
        };

        _db.Organizations.Add(org);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SuperAdmin created org {OrgId} ({Name}) plan={Plan} licenses={Lic}",
            org.Id, org.Name, plan, req.PurchasedLicenses);

        return await GetOrgAsync(org.Id, ct);
    }

    // ── Uppdatera ─────────────────────────────────────────────────────────

    public async Task<OrgAdminDto> UpdateOrgAsync(
        Guid id, UpdateOrgRequest req, CancellationToken ct = default)
    {
        var org = await _db.Organizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException($"Organization {id} not found.");

        if (req.Name is not null)
            org.Name = req.Name.Trim();

        if (req.Plan is not null)
        {
            if (!Enum.TryParse<SubscriptionPlan>(req.Plan, ignoreCase: true, out var plan))
                throw new ArgumentException($"Unknown plan '{req.Plan}'.");
            org.Plan = plan;
        }

        if (req.PurchasedLicenses.HasValue)
            org.PurchasedLicenses = req.PurchasedLicenses.Value;

        if (req.BillingEmail is not null)
            org.BillingEmail = req.BillingEmail.Trim();

        if (req.IsActive.HasValue)
            org.IsActive = req.IsActive.Value;

        if (req.EnabledFeatures is not null)
            org.FeaturesJson = SerializeFeatures(req.EnabledFeatures);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SuperAdmin updated org {OrgId}: plan={Plan} licenses={Lic} active={Active}",
            id, org.Plan, org.PurchasedLicenses, org.IsActive);

        return await GetOrgAsync(id, ct);
    }

    // ── Aktivera / Deaktivera ─────────────────────────────────────────────

    public async Task DeactivateOrgAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var org = await _db.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException($"Organization {id} not found.");

        org.IsActive = false;
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "SuperAdmin deactivated org {OrgId} ({Name}). Reason: {Reason}",
            id, org.Name, reason);
    }

    public async Task ReactivateOrgAsync(Guid id, CancellationToken ct = default)
    {
        var org = await _db.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException($"Organization {id} not found.");

        org.IsActive = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> GetOrgCountAsync(CancellationToken ct = default) =>
        await _db.Organizations.IgnoreQueryFilters().CountAsync(ct);

    // ═══════════════════════════════════════════════════════════════════════
    // Privat hjälp
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<Dictionary<Guid, OrgStats>> BuildOrgStatsAsync(
        IReadOnlyList<Guid> orgIds, CancellationToken ct)
    {
        // Antal aktiva members per org
        var memberCounts = await _db.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(m => orgIds.Contains(m.OrganizationId) && m.IsActive)
            .GroupBy(m => m.OrganizationId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrgId, x => x.Count, ct);

        // Antal projekt per org (via TenantId på Project)
        var projectCounts = await _db.Projects
            .IgnoreQueryFilters()
            .Where(p => orgIds.Contains(p.TenantId) && !p.IsDeleted)
            .GroupBy(p => p.TenantId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrgId, x => x.Count, ct);

        // Öppna issues per org
        var issueCounts = await _db.BacklogItems
            .IgnoreQueryFilters()
            .Where(i => orgIds.Contains(i.TenantId) && !i.IsDeleted
                        && i.Status == BacklogStatus.Open)
            .GroupBy(i => i.TenantId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrgId, x => x.Count, ct);

        return orgIds.ToDictionary(
            id => id,
            id => new OrgStats(
                memberCounts.GetValueOrDefault(id),
                projectCounts.GetValueOrDefault(id),
                issueCounts.GetValueOrDefault(id)));
    }

    private static OrgAdminDto MapToDto(Organization org, OrgStats? stats) => new()
    {
        Id                = org.Id,
        Name              = org.Name,
        Slug              = org.Slug,
        Plan              = org.Plan.ToString(),
        PurchasedLicenses = org.PurchasedLicenses,
        ActiveMembers     = stats?.Members ?? 0,
        TotalProjects     = stats?.Projects ?? 0,
        OpenIssues        = stats?.Issues ?? 0,
        IsActive          = org.IsActive,
        IsOnTrial         = org.TrialEndsAt > DateTime.UtcNow,
        TrialEndsAt       = org.TrialEndsAt,
        BillingEmail      = org.BillingEmail,
        CreatedAt         = org.CreatedAt,
        EnabledFeatures   = DeserializeFeatures(org.FeaturesJson)
    };

    private static string? SerializeFeatures(IReadOnlyList<string> features) =>
        features.Count == 0 ? null :
        System.Text.Json.JsonSerializer.Serialize(features);

    private static IReadOnlyList<string> DeserializeFeatures(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return System.Text.Json.JsonSerializer
            .Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private sealed record OrgStats(int Members, int Projects, int Issues);
}
