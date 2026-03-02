using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Synthtax.Vsix.Auth;

/// <summary>
/// Lagrar JWT-token säkert i Windows Credential Manager
/// via Visual Studios <c>IVsPasswordKeyRing</c>.
///
/// <para>Windows Credential Manager krypterar data med DPAPI per Windows-konto
/// — token är aldrig synlig i klartext på disk eller i registret.</para>
/// </summary>
internal sealed class CredentialStore
{
    private const string TokenKey    = "Synthtax.AccessToken";
    private const string ExpiryKey   = "Synthtax.TokenExpiry";
    private const string BaseUrlKey  = "Synthtax.ApiBaseUrl";
    private const string UserNameKey = "Synthtax.UserName";

    private readonly IVsPasswordKeyRing? _keyRing;

    public CredentialStore(IServiceProvider serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _keyRing = serviceProvider.GetService(typeof(SVsPasswordKeyRing)) as IVsPasswordKeyRing;
    }

    // ── Token ──────────────────────────────────────────────────────────────

    public void SaveToken(string token, DateTime expiresAt)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteSecure(TokenKey, token);
        WriteSecure(ExpiryKey, expiresAt.ToString("O"));
    }

    public (string? Token, DateTime? ExpiresAt) LoadToken()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var token  = ReadSecure(TokenKey);
        var expiry = ReadSecure(ExpiryKey);

        if (token is null) return (null, null);
        DateTime? exp = DateTime.TryParse(expiry, out var dt) ? dt : null;
        return (token, exp);
    }

    public void ClearToken()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        DeleteSecure(TokenKey);
        DeleteSecure(ExpiryKey);
    }

    // ── Inställningar ──────────────────────────────────────────────────────

    public void SaveApiBaseUrl(string url)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteSecure(BaseUrlKey, url);
    }

    public string LoadApiBaseUrl(string defaultUrl = "https://api.synthtax.io")
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadSecure(BaseUrlKey) ?? defaultUrl;
    }

    public void SaveUserName(string userName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteSecure(UserNameKey, userName);
    }

    public string? LoadUserName()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadSecure(UserNameKey);
    }

    // ── Privat: IVsPasswordKeyRing wrapper ───────────────────────────────

    private void WriteSecure(string key, string value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_keyRing is null)
        {
            // Fallback: isolatedStorage (sämre säkerhet, men fungerar om keyRing saknas)
            FallbackWrite(key, value);
            return;
        }
        _keyRing.AddPassword(key, value);
    }

    private string? ReadSecure(string key)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_keyRing is null) return FallbackRead(key);

        _keyRing.GetPassword(key, out var value);
        return value;
    }

    private void DeleteSecure(string key)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_keyRing is null) { FallbackDelete(key); return; }

        try { _keyRing.RemovePassword(key); }
        catch { /* Nyckel kanske inte finns */ }
    }

    // ── Fallback: IsolatedStorage + DPAPI ─────────────────────────────────

    private static string FallbackPath(string key)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Synthtax", "VS");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Convert.ToHexString(Encoding.UTF8.GetBytes(key)) + ".enc");
    }

    private static void FallbackWrite(string key, string value)
    {
        var data      = Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FallbackPath(key), encrypted);
    }

    private static string? FallbackRead(string key)
    {
        var path = FallbackPath(key);
        if (!File.Exists(path)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(path);
            var data      = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch { return null; }
    }

    private static void FallbackDelete(string key)
    {
        var path = FallbackPath(key);
        if (File.Exists(path)) File.Delete(path);
    }
}
