using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using StudyHub.Api.Auth;

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
