using System.Runtime.CompilerServices;

namespace Synthtax.Core.FuzzyMatching;

// ═══════════════════════════════════════════════════════════════════════════
// NgramGenerator
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Genererar n-grams från en tokenlista.
/// Används som input till MinHash — var n-gram representerar en "shingle".
///
/// <para>Eksempel med n=2 (bigrams):
/// <code>
///   ["IF", "($I", "!=", "NULL", ")"]
///   → ["IF $I", "$I !=", "!= NULL", "NULL )"]
/// </code>
/// </para>
/// </summary>
public static class NgramGenerator
{
    /// <summary>
    /// Returnerar n-gram från tokenlistan som en HashSet (unika shingles).
    /// HashSet tar bort dubbletter — rätt för Jaccard-beräkning.
    /// </summary>
    public static HashSet<string> GetShingles(
        IReadOnlyList<string> tokens,
        int n = 2)
    {
        if (tokens.Count < n) return [string.Join(" ", tokens)];

        var shingles = new HashSet<string>(capacity: tokens.Count);
        for (int i = 0; i <= tokens.Count - n; i++)
        {
            var sb = new System.Text.StringBuilder();
            for (int j = 0; j < n; j++)
            {
                if (j > 0) sb.Append(' ');
                sb.Append(tokens[i + j]);
            }
            shingles.Add(sb.ToString());
        }
        return shingles;
    }

    /// <summary>
    /// Unigrams + bigrams kombinerat — bättre täckning för korta snippets.
    /// </summary>
    public static HashSet<string> GetCombinedShingles(IReadOnlyList<string> tokens)
    {
        var result = GetShingles(tokens, 1);
        if (tokens.Count >= 2)
        {
            foreach (var bigram in GetShingles(tokens, 2))
                result.Add(bigram);
        }
        return result;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// MinHashSignature
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Beräknar en MinHash-signatur för en mängd shingles.
///
/// <para><b>Algoritm:</b>
/// <list type="number">
///   <item>Generera k hash-funktioner: <c>h_i(x) = (a_i * x + b_i) mod p</c>
///         där p är ett primtal och a_i, b_i är pseudoslumpmässiga konstanter.</item>
///   <item>För varje hash-funktion, hitta minimumvärdet över alla shingles.</item>
///   <item>Signaturen är vektorn av k minimivärden.</item>
///   <item>Jaccard-estimat: andelen positioner där två signaturer är identiska.</item>
/// </list>
/// </para>
///
/// <para><b>Noggrannhet:</b> Med k=128 är standard-avvikelsen för Jaccard-estimatet
/// ca 1/√128 ≈ 0.088. För threshold 0.85 är detta tillräckligt.</para>
/// </summary>
public sealed class MinHashSignature
{
    // Antal hash-funktioner — balans mellan noggrannhet och minnesfotavtryck
    public const int K = 128;

    // Mersenne-primtal för modulooperationer (2^31 − 1)
    private const long MersennePrime = 2_147_483_647L;

    // Förberäknade pseudoslumpmässiga konstanter (a_i, b_i) för k hash-funktioner
    private static readonly (long A, long B)[] HashParams;

    static MinHashSignature()
    {
        HashParams = new (long A, long B)[K];
        // Deterministisk seeding — INTE System.Random med tidsseed
        var rng = new DeterministicRng(seed: 0xDEAD_BEEF_1337_C0DEL);
        for (int i = 0; i < K; i++)
            HashParams[i] = (rng.Next(1, MersennePrime), rng.Next(0, MersennePrime));
    }

    // Signaturvektorn: K uint-värden
    public readonly uint[] Values;

    private MinHashSignature(uint[] values) { Values = values; }

    /// <summary>
    /// Beräknar MinHash-signatur för en mängd shingles.
    /// </summary>
    public static MinHashSignature Compute(IEnumerable<string> shingles)
    {
        var sig = new uint[K];
        Array.Fill(sig, uint.MaxValue);

        foreach (var shingle in shingles)
        {
            var h = FnvHash(shingle);
            for (int i = 0; i < K; i++)
            {
                var (a, b) = HashParams[i];
                var hashed = (uint)(((long)h * a + b) % MersennePrime);
                if (hashed < sig[i]) sig[i] = hashed;
            }
        }

        return new MinHashSignature(sig);
    }

    /// <summary>
    /// Estimerar Jaccard-likhet mellan två signaturer.
    /// Returnerar andelen positioner med identiska minimivärden [0.0, 1.0].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double EstimateJaccard(MinHashSignature other)
    {
        int matches = 0;
        for (int i = 0; i < K; i++)
            if (Values[i] == other.Values[i]) matches++;
        return (double)matches / K;
    }

    /// <summary>
    /// FNV-1a 32-bit hash — snabb, god distribution, deterministisk.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FnvHash(string s)
    {
        uint hash = 2166136261u;
        foreach (char c in s)
        {
            hash ^= (byte)c;
            hash *= 16777619u;
            // Högre byte
            hash ^= (byte)(c >> 8);
            hash *= 16777619u;
        }
        return hash;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// DeterministicRng  —  XorShift64 PRNG
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Deterministisk pseudo-slumptalgenerator (XorShift64).
/// Används för att generera hash-parametrar — deterministisk seed garanterar
/// att identiska snippets alltid ger identisk signatur, oavsett starttid.
/// </summary>
internal sealed class DeterministicRng
{
    private ulong _state;

    public DeterministicRng(ulong seed) { _state = seed == 0 ? 1 : seed; }

    public long Next(long min, long max)
    {
        _state ^= _state << 13;
        _state ^= _state >> 7;
        _state ^= _state << 17;
        return min + (long)(_state % (ulong)(max - min));
    }
}
