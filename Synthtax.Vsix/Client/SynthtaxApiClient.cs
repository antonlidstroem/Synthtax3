using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Synthtax.Vsix.Auth;

namespace Synthtax.Vsix.Client;

/// <summary>
/// Typed HTTP-klient mot Synthtax API.
///
/// <para>Lever som Singleton i paketet. Skapar en ny HttpClient per session
/// (efter inloggning) och återanvänder den för alla requests under sessionen.</para>
///
/// <para><b>Felhantering:</b>
/// <list type="bullet">
///   <item>401 → token utgången → kastar <see cref="UnauthorizedException"/>.</item>
///   <item>402 → licensgräns → kastar <see cref="LicenseException"/>.</item>
///   <item>Timeout / nätverksfel → kastar <see cref="SynthtaxApiException"/>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SynthtaxApiClient : IDisposable
{
    private readonly AuthTokenService _auth;
    private HttpClient? _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    public SynthtaxApiClient(AuthTokenService auth)
    {
        _auth = auth;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Auth
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loggar in och sparar JWT. Skapar en ny HttpClient med token.
    /// </summary>
    public async Task<AuthResponseDto> LoginAsync(
        string username, string password, CancellationToken ct = default)
    {
        // Inloggning sker utan Authorization-header
        using var tempClient = CreateHttpClient(withAuth: false);

        var body = JsonContent.Create(new { username, password });
        var resp = await tempClient.PostAsync("api/v1/auth/login", body, ct);
        resp.EnsureSuccessStatusCode();

        var authResp = await resp.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOpts, ct)
                       ?? throw new SynthtaxApiException("Empty auth response");

        // Spara token säkert
        await _auth.SaveTokenAsync(authResp.AccessToken, authResp.ExpiresAt);

        // Ny klient med token
        _http?.Dispose();
        _http = CreateHttpClient(withAuth: true);

        return authResp;
    }

    /// <summary>Loggar ut och rensar sparad token.</summary>
    public async Task LogoutAsync()
    {
        await _auth.ClearTokenAsync();
        _http?.Dispose();
        _http = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Backlog
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hämtar paginerat backlog för inloggad org, filtrerat på status Open/Acknowledged/InProgress.
    /// </summary>
    public async Task<PagedBacklogDto> GetBacklogAsync(
        int page = 1, int pageSize = 50, string? severity = null, CancellationToken ct = default)
    {
        var client = GetAuthenticatedClient();
        var url    = BuildUrl("api/v1/backlog",
                              ("page", page.ToString()),
                              ("pageSize", pageSize.ToString()),
                              ("status", "open"),
                              ("severity", severity ?? ""));

        var resp = await client.GetAsync(url, ct);
        await EnsureSuccessOrThrowAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<PagedBacklogDto>(JsonOpts, ct)
               ?? new PagedBacklogDto();
    }

    /// <summary>Hämtar projekthälsa för aktuell org.</summary>
    public async Task<ProjectHealthDto> GetProjectHealthAsync(CancellationToken ct = default)
    {
        var resp = await GetAuthenticatedClient().GetAsync("api/v1/analysis/health", ct);
        await EnsureSuccessOrThrowAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<ProjectHealthDto>(JsonOpts, ct)
               ?? new ProjectHealthDto();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Prompt Factory
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Begär en AI-prompt från API:t för ett backlog-ärende.
    /// </summary>
    /// <param name="itemId">BacklogItem ID.</param>
    /// <param name="target">"Copilot" eller "Claude".</param>
    public async Task<PromptResponseDto> GetPromptAsync(
        Guid itemId, string target = "Copilot", CancellationToken ct = default)
    {
        var url  = $"api/v1/prompts/generate?itemId={itemId:D}&target={target}";
        var resp = await GetAuthenticatedClient().PostAsync(url, null, ct);
        await EnsureSuccessOrThrowAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<PromptResponseDto>(JsonOpts, ct)
               ?? new PromptResponseDto();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Privat hjälp
    // ═══════════════════════════════════════════════════════════════════════

    private HttpClient GetAuthenticatedClient() =>
        _http ?? throw new UnauthorizedException("Inte inloggad. Logga in via Synthtax → Logga in.");

    private HttpClient CreateHttpClient(bool withAuth)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(_auth.ApiBaseUrl),
            Timeout     = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        if (withAuth)
        {
            var token = _auth.GetCachedToken();
            if (token is not null)
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        throw resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new UnauthorizedException(
                "Session utgången. Logga in igen via Synthtax → Logga in."),
            HttpStatusCode.PaymentRequired => new LicenseException(
                TryExtractField(body, "error") ?? "Licensgräns nådd. Uppgradera planen."),
            HttpStatusCode.NotFound => new SynthtaxApiException($"Resurs hittades inte (404)."),
            _ => new SynthtaxApiException(
                $"API-fel {(int)resp.StatusCode}: {TryExtractField(body, "error") ?? body[..Math.Min(200, body.Length)]}")
        };
    }

    private static string? TryExtractField(string json, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var el) ? el.GetString() : null;
        }
        catch { return null; }
    }

    private static string BuildUrl(string path, params (string Key, string Value)[] query)
    {
        var sb = new StringBuilder(path).Append('?');
        foreach (var (k, v) in query)
            if (!string.IsNullOrEmpty(v))
                sb.Append(Uri.EscapeDataString(k)).Append('=')
                  .Append(Uri.EscapeDataString(v)).Append('&');
        return sb.ToString().TrimEnd('&', '?');
    }

    public void Dispose() => _http?.Dispose();
}

// ── Custom exceptions ──────────────────────────────────────────────────────

public sealed class SynthtaxApiException(string message) : Exception(message);
public sealed class UnauthorizedException(string message) : Exception(message);
public sealed class LicenseException(string message) : Exception(message);
