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

        _logger.LogInformation("ApiClient initialized. BaseAddress: {BaseAddress}", _http.BaseAddress);
    }

    // ── Auth ─────────────────────────────────────────────────────────────────

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        _logger.LogInformation("Attempting login for user: {UserName}", dto.UserName);

        var response = await PostRawAsync("api/auth/login", dto, skipAuth: true, ct: ct);
        if (response is null)
        {
            _logger.LogError("LoginAsync: PostRawAsync returned null – network error or exception. Check logs above.");
            return null;
        }

        _logger.LogInformation("Login response status: {StatusCode}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Login failed. Status: {StatusCode}. Body: {Body}",
                response.StatusCode, errorBody);
            return null;
        }

        var result = await DeserializeAsync<AuthResponseDto>(response, "api/auth/login");
        if (result is not null)
        {
            _tokenStore.AccessToken = result.AccessToken;
            _tokenStore.RefreshToken = result.RefreshToken;
            _tokenStore.CurrentUser = result.User;
            _logger.LogInformation("Login succeeded for user: {UserName}", dto.UserName);
        }
        return result;
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

    /// <summary>
    /// Internal POST that returns the raw HttpResponseMessage so callers
    /// like LoginAsync can inspect status/body before deserializing.
    /// </summary>
    private async Task<HttpResponseMessage?> PostRawAsync(string url, object? body,
        bool skipAuth, CancellationToken ct)
    {
        var request = BuildRequest(HttpMethod.Post, url, body, skipAuth);
        return await SendWithRefreshAsync(request, ct, skipAuth);
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
                        var retryRequest = BuildRequest(request.Method,
                            request.RequestUri!.ToString());

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

                _tokenStore.Clear();
                SessionExpired?.Invoke(this, EventArgs.Empty);
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            // Nätverksfel – API är troligen nere eller BaseAddress är fel
            _logger.LogError(ex,
                "Network error sending {Method} {Url}. " +
                "Check that the API is running and that BaseAddress ({BaseAddress}) is correct.",
                request.Method, request.RequestUri, _http.BaseAddress);
            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Request timed out: {Method} {Url}", request.Method, request.RequestUri);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending {Method} {Url}",
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
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token refresh failed with status {StatusCode}", response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var auth = JsonConvert.DeserializeObject<AuthResponseDto>(content);
            if (auth is null) return false;

            _tokenStore.AccessToken = auth.AccessToken;
            _tokenStore.RefreshToken = auth.RefreshToken;
            _tokenStore.CurrentUser = auth.User;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during token refresh.");
            return false;
        }
    }

    private async Task<T?> DeserializeAsync<T>(HttpResponseMessage? response, string url)
    {
        if (response is null) return default;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("API returned {StatusCode} for {Url}. Body: {Body}",
                response.StatusCode, url, body);
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
