using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;
using StudyHub.Api.Services;

namespace StudyHub.Api.Tests;

// VI: Mỗi factory dùng một file SQLite tạm riêng, xóa khi xong.
// EN: Each factory uses its own temporary SQLite file, deleted afterwards.
public class StudyHubFactory : WebApplicationFactory<Program>
{
    public const string SuperAdminUsername = "rootadmin";
    public const string TestJwtKey = "test-only-key-0123456789-abcdefghijklmnopqrstuvwxyz";
    private readonly SemaphoreSlim _adminLock = new(1, 1);
    private string? _adminToken;

    // VI: Mail "gửi" trong test nằm ở đây thay vì đi qua SMTP. EN: Mail "sent" during tests lands here instead of SMTP.
    public FakeEmailSender Outbox { get; } = new();

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"studyhub-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Key", TestJwtKey);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath};Pooling=false");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "1000");
        builder.UseSetting("RateLimiting:PublicProfilePermitsPerMinute", "1000");
        builder.UseSetting("Admin:SuperAdmins:0", SuperAdminUsername);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender>(Outbox);
            services.AddSingleton<IStartupFilter, TestIpStartupFilter>();
        });
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
                // VI: Server đã tạo sẵn tài khoản lúc khởi động với mật khẩu ngẫu nhiên; test đặt lại mật khẩu đã biết.
                // EN: The server created the account at start-up with a random password; the test sets a known one.
                using (var scope = Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
                    var admin = await db.Users.SingleAsync(u => u.Username == SuperAdminUsername);
                    admin.PasswordHash = hasher.HashPassword(admin, "password123");
                    await db.SaveChangesAsync();
                }
                var res = await CreateClient().PostAsJsonAsync("/api/auth/login",
                    new LoginRequest(SuperAdminUsername, "password123"));
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

// VI: Header X-Test-Ip giả lập IP người gọi (TestServer không có IP thật). EN: X-Test-Ip fakes the caller's IP (TestServer has none).
public class TestIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nxt) =>
        {
            if (ctx.Request.Headers.TryGetValue(Header, out var ip))
                ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip.ToString());
            return nxt(ctx);
        });
        next(app);
    };
}

public class FakeEmailSender : IEmailSender
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<EmailMessage> _sent = new();

    public bool IsConfigured { get; set; } = true;
    public IReadOnlyList<EmailMessage> Sent => _sent.ToList();

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public List<EmailMessage> To(string email) => Sent.Where(m => m.To == email).ToList();
}
