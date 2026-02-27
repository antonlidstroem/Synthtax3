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

    // FIX: Use a SemaphoreSlim instead of a plain bool flag to prevent
    // concurrent refresh attempts. If two requests get 401 at the same time,
    // the bool flag is not thread-safe and both would try to refresh,
    // which revokes the first new refresh token before it's used.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private bool _isRefreshing;

    public ApiClient(HttpClient http, ILogger<ApiClient> logger, TokenStore tokenStore)
    {
        _http = http;
        _logger = logger;
        _tokenStore = tokenStore;

        _logger.LogInformation("ApiClient initialized. BaseAddress: {BaseAddress}", _http.BaseAddress);
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

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

    // ── Generic HTTP helpers ──────────────────────────────────────────────────

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

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Serializes <paramref name="body"/> to JSON and stores it as a string
    /// so it can be re-read when the request must be retried after a token refresh.
    /// HttpRequestMessage.Content can only be read once; we stash the raw JSON.
    /// </summary>
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

    private async Task<HttpResponseMessage?> PostRawAsync(string url, object? body,
        bool skipAuth, CancellationToken ct)
    {
        var request = BuildRequest(HttpMethod.Post, url, body, skipAuth);
        return await SendWithRefreshAsync(request, ct, skipAuth);
    }

    private async Task<HttpResponseMessage?> SendWithRefreshAsync(
        HttpRequestMessage request, CancellationToken ct, bool skipAuth = false)
    {
        // Capture body JSON before first send so we can replay it on retry.
        // HttpContent can only be consumed once, so read it now.
        string? bodyJson = null;
        if (request.Content is not null)
            bodyJson = await request.Content.ReadAsStringAsync(ct);

        try
        {
            var response = await _http.SendAsync(request, ct);

            // ── 401: try to refresh the token once ────────────────────────────
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                && !skipAuth
                && _tokenStore.RefreshToken is not null)
            {
                // FIX: Use a semaphore so only one concurrent refresh runs.
                // With the old bool flag, two simultaneous 401s both entered
                // the refresh block, the second revoked the first new token.
                await _refreshLock.WaitAsync(ct);
                try
                {
                    if (!_isRefreshing)
                    {
                        _isRefreshing = true;
                        var refreshed = await TryRefreshTokenAsync(ct);

                        if (refreshed)
                        {
                            // Rebuild the request with the NEW access token and original body.
                            // FIX: The old code tried to read request.Content.ReadAsStringAsync
                            // AFTER it had already been sent, which returns empty string because
                            // the stream is exhausted. We now use the bodyJson captured above.
                            var retry = new HttpRequestMessage(request.Method,
                                request.RequestUri!.ToString());

                            retry.Headers.Authorization =
                                new AuthenticationHeaderValue("Bearer", _tokenStore.AccessToken);

                            if (bodyJson is not null)
                                retry.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

                            _isRefreshing = false;
                            return await _http.SendAsync(retry, ct);
                        }

                        _isRefreshing = false;
                    }
                }
                finally
                {
                    _refreshLock.Release();
                }

                // Refresh failed – session is dead.
                _tokenStore.Clear();
                SessionExpired?.Invoke(this, EventArgs.Empty);
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Network error sending {Method} {Url}. " +
                "Check that the API is running and that BaseAddress ({BaseAddress}) is correct.",
                request.Method, request.RequestUri, _http.BaseAddress);
            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Request timed out: {Method} {Url}",
                request.Method, request.RequestUri);
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
            var body = JsonConvert.SerializeObject(
                new RefreshTokenDto { RefreshToken = _tokenStore.RefreshToken! });

            var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            // NOTE: No Authorization header – refresh is an anonymous endpoint.
            var response = await _http.SendAsync(refreshRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token refresh failed with status {StatusCode}",
                    response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var auth = JsonConvert.DeserializeObject<AuthResponseDto>(content);
            if (auth is null) return false;

            _tokenStore.AccessToken = auth.AccessToken;
            _tokenStore.RefreshToken = auth.RefreshToken;
            _tokenStore.CurrentUser = auth.User;

            _logger.LogInformation("Token refreshed successfully.");
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

    /// <summary>Raised when a session expires and cannot be refreshed.</summary>
    public event EventHandler? SessionExpired;
}
