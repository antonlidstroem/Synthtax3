using Synthtax.Core.Contracts;

namespace Synthtax.Core.Fingerprinting;

/// <summary>
/// Typad, immutabel input till <see cref="IFingerprintService"/>.
///
/// <para><b>Alla fyra komponenter krävs för ett stabilt fingerprint:</b>
/// <list type="bullet">
///   <item><see cref="ProjectId"/> — isolerar fingerprints mellan projekt.</item>
///   <item><see cref="RuleId"/> — samma kodlukt detekterad av två regler ska ge olika fingerprints.</item>
///   <item><see cref="Scope"/> — semantisk plats (metod/klass) — stabil vid radändringar.</item>
///   <item><see cref="RawSnippet"/> — normaliseras och hashas för att fånga exakt vilket konstrukt som är problematiskt.</item>
/// </list>
/// </para>
/// </summary>
public sealed record FingerprintInput
{
    /// <summary>Projektets databas-ID.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Regelns ID, t.ex. "CA001".</summary>
    public required string RuleId { get; init; }

    /// <summary>Semantisk plats — se <see cref="LogicalScope"/>.</summary>
    public required LogicalScope Scope { get; init; }

    /// <summary>
    /// Råt kodsnippet från plugin — normaliseras av FingerprintService
    /// innan hashing. Plugin ska INTE normalisera detta.
    /// </summary>
    public required string RawSnippet { get; init; }

    /// <summary>
    /// Filändelse, t.ex. ".cs" eller ".py".
    /// Styr val av kommentarssyntax i normaliseraren.
    /// </summary>
    public string? FileExtension { get; init; }

    // ── Factory ────────────────────────────────────────────────────────────

    /// <summary>Skapar en <see cref="FingerprintInput"/> direkt från en <see cref="RawIssue"/>.</summary>
    public static FingerprintInput FromRawIssue(RawIssue issue, Guid projectId) =>
        new()
        {
            ProjectId     = projectId,
            RuleId        = issue.RuleId,
            Scope         = issue.Scope,
            RawSnippet    = issue.Snippet,
            FileExtension = Path.GetExtension(issue.FilePath)
        };

    /// <summary>Diagnostisk sträng — visar vad som hashas (utan faktisk hash).</summary>
    public override string ToString() =>
        $"Project:{ProjectId:N} | Rule:{RuleId} | Scope:{Scope.ToFingerprintKey()} | " +
        $"Snippet[{RawSnippet.Length}chars ext={FileExtension ?? "?"}]";
}
