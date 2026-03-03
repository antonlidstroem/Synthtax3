using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Synthtax.Admin.Models;

namespace Synthtax.Admin.Services;

public class LoginResult
{
    public bool   Success  { get; set; }
    public string Error    { get; set; } = string.Empty;
    public string Role     { get; set; } = string.Empty; // "Admin" | "SuperAdmin"
    public string Token    { get; set; } = string.Empty;
}

public class AuthService
{
    private readonly HttpClient _http;
    public string? CurrentToken { get; private set; }
    public string? CurrentRole  { get; private set; }

    public AuthService(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/api/auth/login", new { username, password });
            if (!resp.IsSuccessStatusCode)
                return new LoginResult { Error = $"HTTP {(int)resp.StatusCode}" };

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var token = json.GetProperty("token").GetString() ?? "";
            var role  = json.TryGetProperty("role", out var r) ? r.GetString() ?? "Admin" : "Admin";

            CurrentToken = token;
            CurrentRole  = role;
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            return new LoginResult { Success = true, Token = token, Role = role };
        }
        catch (Exception ex)
        {
            return new LoginResult { Error = ex.Message };
        }
    }

    public void Logout()
    {
        CurrentToken = null;
        CurrentRole  = null;
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public HttpClient Http => _http;
}
