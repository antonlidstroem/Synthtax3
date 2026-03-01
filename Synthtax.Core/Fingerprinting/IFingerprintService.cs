namespace Synthtax.Core.Fingerprinting;

/// <summary>
/// Kontraktet för fingerprint-beräkning.
/// Registreras som Singleton — implementeringen är stateless.
/// </summary>
public interface IFingerprintService
{
    /// <summary>
    /// Beräknar ett deterministiskt SHA-256 fingerprint för given input.
    /// Returnerar 64 hexadecimala lowercase-tecken.
    /// </summary>
    string Compute(FingerprintInput input);

    /// <summary>
    /// Batchberäkning — effektivare för sessioner med många issues.
    /// Ordningen på output motsvarar ordningen på input.
    /// </summary>
    IReadOnlyList<string> ComputeBatch(IReadOnlyList<FingerprintInput> inputs);

    /// <summary>
    /// Beräknar fingerprint och returnerar också den normaliserade snippet
    /// som faktiskt hashades. Användbart för debug och audit.
    /// </summary>
    (string Hash, string NormalizedSnippet, string PreHashKey) ComputeWithDiagnostics(
        FingerprintInput input);
}
