using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using StudyHub.Api.Auth;
using StudyHub.Api.Dtos;

namespace StudyHub.Api.Tests;

// VI: Không có khóa JWT trong appsettings, nên thiếu khóa thì server phải dừng ngay với thông báo rõ ràng.
// EN: There is no JWT key in appsettings, so a missing key must stop the server at once with a clear message.
public class JwtKeyTests
{
    private sealed class FactoryWithKey(string key) : StudyHubFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:Key", key);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short-key")]
    public void Server_refuses_to_start_without_a_valid_key(string key)
    {
        using var factory = new FactoryWithKey(key);
        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains(JwtOptions.MissingKeyMessage, ex.Message);
    }

    private sealed class FactoryWithEnvFileNames : StudyHubFactory
    {
        public const string AdminName = "nasadmin";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:Key", "");
            builder.UseSetting("Admin:SuperAdmins:0", "");
            builder.UseSetting("JWT_KEY", "env-file-key-0123456789-abcdefghijklmnopqrstuvwxyz");
            builder.UseSetting("SUPERADMIN_USERNAME", AdminName);
        }
    }

    // VI: `docker run --env-file .env` chỉ truyền JWT_KEY / SUPERADMIN_USERNAME, app phải nhận được.
    // EN: `docker run --env-file .env` only passes JWT_KEY / SUPERADMIN_USERNAME; the app must accept them.
    [Fact]
    public async Task Env_file_names_alone_are_accepted()
    {
        using var factory = new FactoryWithEnvFileNames();
        var client = factory.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var reserved = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(FactoryWithEnvFileNames.AdminName, "nas@example.com", "Password123!"));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, reserved.StatusCode);

        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("envuser", "envuser@example.com", "Password123!"));
        Assert.Equal(System.Net.HttpStatusCode.Created, reg.StatusCode);
    }

    [Fact]
    public void Development_settings_file_has_no_jwt_key()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "StudyHub.slnx"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var json = File.ReadAllText(Path.Combine(dir, "src", "StudyHub.Api", "appsettings.Development.json"));
        Assert.DoesNotContain("\"Jwt\"", json);
    }
}
