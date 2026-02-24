using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Synthtax.Core.DTOs;

namespace Synthtax.WPF.Services;

/// <summary>
/// Central HTTP client for all API communication.
/// Automatically refreshes JWT tokens on 401 responses.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ApiClient> _logger;
    private readonly TokenStore _tokenStore;

    private bool _isRefreshing;

    public ApiClient(HttpClient http, ILogger<ApiClient> logger, TokenStore tokenStore)
    {
        _http = http;
        _logger = logger;
        _tokenStore = tokenStore;
    }

    // ── Auth ─────────────────────────────────────────────────────────────────

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var response = await PostAsync<AuthResponseDto>("api/auth/login", dto, skipAuth: true, ct: ct);
        if (response is not null)
        {
            _tokenStore.AccessToken = response.AccessToken;
            _tokenStore.RefreshToken = response.RefreshToken;
            _tokenStore.CurrentUser = response.User;
        }
        return response;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            if (_tokenStore.RefreshToken is not null)
                await PostAsync<object>("api/auth/logout",
                    new RefreshTokenDto { RefreshToken = _tokenStore.RefreshToken }, ct: ct);
        }
        finally
        {
            _tokenStore.Clear();
        }
    }

    // ── Generic HTTP helpers ─────────────────────────────────────────────────

    public async Task<T?> GetAsync<T>(string url, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Get, url);
        var response = await SendWithRefreshAsync(request, ct);
        return await DeserializeAsync<T>(response, url);
    }

    public async Task<T?> PostAsync<T>(string url, object? body = null,
        bool skipAuth = false, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, url, body, skipAuth);
        var response = await SendWithRefreshAsync(request, ct, skipAuth);
        return await DeserializeAsync<T>(response, url);
    }

    public async Task<T?> PutAsync<T>(string url, object? body = null, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Put, url, body);
        var response = await SendWithRefreshAsync(request, ct);
        return await DeserializeAsync<T>(response, url);
    }

    public async Task<T?> PatchAsync<T>(string url, object? body = null, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Patch, url, body);
        var response = await SendWithRefreshAsync(request, ct);
        return await DeserializeAsync<T>(response, url);
    }

    public async Task<bool> DeleteAsync(string url, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Delete, url);
        var response = await SendWithRefreshAsync(request, ct);
        return response?.IsSuccessStatusCode ?? false;
    }

    /// <summary>Downloads a file and returns raw bytes (for export endpoints).</summary>
    public async Task<(byte[]? data, string? contentType, string? fileName)> DownloadAsync(
        string url, object? body = null, CancellationToken ct = default)
    {
        var method = body is null ? HttpMethod.Get : HttpMethod.Post;
        var request = BuildRequest(method, url, body);
        var response = await SendWithRefreshAsync(request, ct);

        if (response is null || !response.IsSuccessStatusCode) return (null, null, null);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

        return (bytes, contentType, fileName);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private HttpRequestMessage BuildRequest(HttpMethod method, string url,
        object? body = null, bool skipAuth = false)
    {
        var request = new HttpRequestMessage(method, url);

        if (!skipAuth && _tokenStore.AccessToken is not null)
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _tokenStore.AccessToken);

        if (body is not null)
        {
            var json = JsonConvert.SerializeObject(body, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<HttpResponseMessage?> SendWithRefreshAsync(
        HttpRequestMessage request, CancellationToken ct, bool skipAuth = false)
    {
        try
        {
            var response = await _http.SendAsync(request, ct);

            // On 401 – try to refresh token once
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                && !skipAuth && !_isRefreshing
                && _tokenStore.RefreshToken is not null)
            {
                _isRefreshing = true;
                try
                {
                    var refreshed = await TryRefreshTokenAsync(ct);
                    if (refreshed)
                    {
                        // Rebuild request (cannot resend consumed request)
                        var retryRequest = BuildRequest(request.Method, request.RequestUri!.ToString());
                        if (request.Content is StringContent sc)
                            retryRequest.Content = new StringContent(
                                await sc.ReadAsStringAsync(ct), Encoding.UTF8, "application/json");

                        return await _http.SendAsync(retryRequest, ct);
                    }
                }
                finally
                {
                    _isRefreshing = false;
                }

                // Refresh failed → user must log in again
                _tokenStore.Clear();
                SessionExpired?.Invoke(this, EventArgs.Empty);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP request failed: {Method} {Url}",
                request.Method, request.RequestUri);
            return null;
        }
    }

    private async Task<bool> TryRefreshTokenAsync(CancellationToken ct)
    {
        try
        {
            var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh");
            var body = JsonConvert.SerializeObject(
                new RefreshTokenDto { RefreshToken = _tokenStore.RefreshToken! });
            refreshRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(refreshRequest, ct);
            if (!response.IsSuccessStatusCode) return false;

            var content = await response.Content.ReadAsStringAsync(ct);
            var auth = JsonConvert.DeserializeObject<AuthResponseDto>(content);
            if (auth is null) return false;

            _tokenStore.AccessToken = auth.AccessToken;
            _tokenStore.RefreshToken = auth.RefreshToken;
            _tokenStore.CurrentUser = auth.User;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<T?> DeserializeAsync<T>(HttpResponseMessage? response, string url)
    {
        if (response is null) return default;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("API returned {StatusCode} for {Url}", response.StatusCode, url);
            return default;
        }

        try
        {
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize response from {Url}", url);
            return default;
        }
    }

    /// <summary>Raised when a session expires and can't be refreshed.</summary>
    public event EventHandler? SessionExpired;
}
