using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StudyHub.Api.Dtos;

namespace StudyHub.Api.Tests;

// VI: Mỗi factory dùng một file SQLite tạm riêng, xóa khi xong.
// EN: Each factory uses its own temporary SQLite file, deleted afterwards.
public class StudyHubFactory : WebApplicationFactory<Program>
{
    public const string SuperAdminUsername = "rootadmin";
    private readonly SemaphoreSlim _adminLock = new(1, 1);
    private string? _adminToken;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"studyhub-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath};Pooling=false");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "1000");
        builder.UseSetting("RateLimiting:PublicProfilePermitsPerMinute", "1000");
        builder.UseSetting("Admin:SuperAdmins:0", SuperAdminUsername);
    }

    public async Task<HttpClient> CreateUserClientAsync(string? username = null)
    {
        var client = CreateClient();
        username ??= "u" + Guid.NewGuid().ToString("N")[..10];
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(username, $"{username}@example.com", "password123"));
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    // VI: Đăng ký super admin cấu hình sẵn một lần cho mỗi factory. EN: Registers the configured super admin once per factory.
    public async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        await _adminLock.WaitAsync();
        try
        {
            if (_adminToken is null)
            {
                var res = await CreateClient().PostAsJsonAsync("/api/auth/register",
                    new RegisterRequest(SuperAdminUsername, "root@example.com", "password123"));
                res.EnsureSuccessStatusCode();
                _adminToken = (await res.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!.AccessToken;
            }
        }
        finally
        {
            _adminLock.Release();
        }
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
