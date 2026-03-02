using Microsoft.VisualStudio.Shell;

namespace Synthtax.Vsix.Auth;

/// <summary>
/// Hanterar JWT-tokenets livscykel: spara, läsa, validera utgångsdatum, rensa.
/// Wrapping:ar <see cref="CredentialStore"/> och exponerar en tunn service-yta.
/// </summary>
public sealed class AuthTokenService
{
    private readonly CredentialStore _store;

    // Cache-fält för att undvika upprepade disk-läsningar per request
    private string?  _cachedToken;
    private DateTime? _cachedExpiry;

    public AuthTokenService(IServiceProvider serviceProvider)
    {
        _store = new CredentialStore(serviceProvider);
        LoadCacheFromStore();
    }

    // ── Publik API ────────────────────────────────────────────────────────

    public bool IsAuthenticated =>
        _cachedToken is not null && (_cachedExpiry is null || _cachedExpiry > DateTime.UtcNow.AddMinutes(5));

    public string ApiBaseUrl
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _store.LoadApiBaseUrl();
        }
    }

    /// <summary>Returnerar token om giltig, annars null.</summary>
    public string? GetCachedToken() =>
        IsAuthenticated ? _cachedToken : null;

    /// <summary>Sparar ny token till säkert lager och uppdaterar cache.</summary>
    public async Task SaveTokenAsync(string token, DateTime expiresAt)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _store.SaveToken(token, expiresAt);
        _cachedToken  = token;
        _cachedExpiry = expiresAt;
    }

    /// <summary>Rensar token från säkert lager och cache.</summary>
    public async Task ClearTokenAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _store.ClearToken();
        _cachedToken  = null;
        _cachedExpiry = null;
    }

    public async Task SaveApiBaseUrlAsync(string url)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _store.SaveApiBaseUrl(url);
    }

    public string? GetSavedUserName()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return _store.LoadUserName();
    }

    public async Task SaveUserNameAsync(string userName)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _store.SaveUserName(userName);
    }

    // ── Privat ────────────────────────────────────────────────────────────

    private void LoadCacheFromStore()
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        var (token, expiry) = _store.LoadToken();
        _cachedToken  = token;
        _cachedExpiry = expiry;
    }
}
