// ══════════════════════════════════════════════════════════════════════════
// FAS 4 — Tillägg till BacklogItem-entiteten
//
// Lägg till dessa properties i BacklogItem-klassen i Entities.cs:
//
//   /// <summary>
//   /// JSON-array av historiska fingerprints.
//   /// Uppdateras av FuzzyAwareSyncWriter när ett fingerprint migreras.
//   /// Format: ["abc123abc123…", "def456def456…"]
//   /// </summary>
//   public string? PreviousFingerprints { get; set; }
//
// ══════════════════════════════════════════════════════════════════════════

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synthtax.Domain.Entities;

namespace Synthtax.Infrastructure.Data.Configurations;

/// <summary>
/// Fas 4-patch av BacklogItemConfiguration.
/// Lägger till <c>PreviousFingerprints</c>-kolumnen.
///
/// Alternativt: lägg in property-definitionen manuellt i BacklogItemConfigurationV3.
/// </summary>
public class BacklogItemConfigurationV4Patch : IEntityTypeConfiguration<BacklogItem>
{
    public void Configure(EntityTypeBuilder<BacklogItem> b)
    {
        // Alla befintliga inställningar ärvs från V3-konfigurationen.
        // Denna klass lägger bara till den nya kolumnen.
        b.Property(bi => bi.PreviousFingerprints)
         .HasColumnType("nvarchar(max)")
         .HasColumnName("PreviousFingerprints");

        // Index för att hitta items baserat på deras fingerprint-historik
        // (används vid diagnostik — inte i hot path)
        // OBS: nvarchar(max) kan inte indexeras direkt i SQL Server.
        // Använd computed column eller fulltext-index om sökning i historik behövs.
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// FingerprintHistoryExtensions
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hjälpmetoder för att läsa och skriva <c>BacklogItem.PreviousFingerprints</c>.
/// </summary>
public static class FingerprintHistoryExtensions
{
    /// <summary>
    /// Returnerar listan av historiska fingerprints för detta item.
    /// Returnerar tom lista om historiken är null eller skadad.
    /// </summary>
    public static IReadOnlyList<string> GetFingerprintHistory(this BacklogItem item)
    {
        if (string.IsNullOrEmpty(item.PreviousFingerprints))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(item.PreviousFingerprints)
                   ?.AsReadOnly()
                ?? (IReadOnlyList<string>)[];
        }
        catch { return []; }
    }

    /// <summary>
    /// Returnerar true om det givna fingerprintet är det aktuella ELLER
    /// finns i fingerprint-historiken. Används för diagnostik.
    /// </summary>
    public static bool HasOrHadFingerprint(this BacklogItem item, string fingerprint)
    {
        if (string.Equals(item.Fingerprint, fingerprint, StringComparison.Ordinal))
            return true;

        return item.GetFingerprintHistory()
            .Any(fp => string.Equals(fp, fingerprint, StringComparison.Ordinal));
    }
}
