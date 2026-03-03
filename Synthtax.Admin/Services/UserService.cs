using System.Net.Http;
using System.Net.Http.Json;
using Synthtax.Admin.Models;

namespace Synthtax.Admin.Services;

public class UserService
{
    private readonly HttpClient _http;

    public UserService(HttpClient http) => _http = http;

    public Task<List<UserModel>?> GetAllUsersAsync()
        => _http.GetFromJsonAsync<List<UserModel>>("/api/admin/users");

    public Task<HttpResponseMessage> DeleteUserAsync(string id)
        => _http.DeleteAsync($"/api/admin/users/{id}");

    public Task<HttpResponseMessage> SetRoleAsync(string id, string role, bool grant)
        => _http.PostAsJsonAsync($"/api/admin/users/{id}/role", new { role, grant });

    public Task<HttpResponseMessage> SetLockedAsync(string id, bool locked)
        => _http.PostAsJsonAsync($"/api/admin/users/{id}/lock", new { locked });

    public Task<HttpResponseMessage> ResetPasswordAsync(string id, string newPassword)
        => _http.PostAsJsonAsync($"/api/admin/users/{id}/reset-password", new { newPassword });
}
