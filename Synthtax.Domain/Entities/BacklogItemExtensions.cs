// ══════════════════════════════════════════════════════════════════════════
// FAS 3 — Tillägg till BacklogItem-entiteten
// Fil: Synthtax.Domain/Entities/BacklogItemExtensions.cs
//
// Lägg till dessa tre properties i BacklogItem-klassen i Entities.cs:
//
//   public bool AutoClosed { get; set; }
//   public Guid? AutoClosedInSessionId { get; set; }
//   public Guid? ReopenedInSessionId   { get; set; }
//
// Denna separata fil innehåller:
//   1. Status-hjälpklassen BacklogStatusHelper (statiska predikates)
//   2. Uppdaterad EF-konfiguration med de nya kolumnerna
// ══════════════════════════════════════════════════════════════════════════

using Synthtax.Core.Enums;
using Synthtax.Domain.Enums;

namespace Synthtax.Domain.Entities;

/// <summary>
/// Statiska predikates för BacklogItem-statusar.
/// Centraliserar affärslogiken: "vad är ett aktivt ärende?"
/// </summary>
public static class BacklogItemStatus
{
    /// <summary>
    /// "Aktiva" statusar — ärenden som fortfarande kräver åtgärd.
    /// Auto-close gäller bara dessa; Accepted/FalsePositive respekteras.
    /// </summary>
    public static readonly IReadOnlySet<BacklogStatus> Active = new HashSet<BacklogStatus>
    {
        BacklogStatus.Open,
        BacklogStatus.Acknowledged,
        BacklogStatus.InProgress
    };

    /// <summary>
    /// Statusar som triggrar Re-open vid nästa match.
    /// Bara AutoClosed Resolved — manuellt resolvade eller Accepted berörs inte.
    /// </summary>
    public static readonly IReadOnlySet<BacklogStatus> ReopenableIfAutoClose = new HashSet<BacklogStatus>
    {
        BacklogStatus.Resolved   // endast om AutoClosed == true
    };

    /// <summary>
    /// Statusar som ALDRIG påverkas av auto-close eller re-open.
    /// Mänskliga beslut respekteras.
    /// </summary>
    public static readonly IReadOnlySet<BacklogStatus> Terminal = new HashSet<BacklogStatus>
    {
        BacklogStatus.Accepted,
        BacklogStatus.FalsePositive
    };

    public static bool IsActive(BacklogStatus status) => Active.Contains(status);

    public static bool IsTerminal(BacklogStatus status) => Terminal.Contains(status);

    /// <summary>
    /// Ska ett matchat ärende återöppnas?
    /// Ja om: status är Resolved OCH det stängdes automatiskt (inte av en människa).
    /// </summary>
    public static bool ShouldReopen(BacklogStatus status, bool autoClosedFlag) =>
        status == BacklogStatus.Resolved && autoClosedFlag;
}
