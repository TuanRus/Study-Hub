using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;

namespace StudyHub.Api.Tests;

// VI: Link công khai qua domain riêng: PUBLIC_BASE_URL và chạy sau reverse proxy (BEHIND_PROXY).
// EN: Public links on a custom domain: PUBLIC_BASE_URL and running behind a reverse proxy (BEHIND_PROXY).
public class PublicSiteTests
{
    private sealed class FactoryWith(params (string Key, string Value)[] settings) : StudyHubFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        }
    }

    [Fact]
    public async Task Without_setting_public_base_url_is_null()
    {
        using var factory = new StudyHubFactory();
        var site = await factory.CreateClient().GetFromJsonAsync<SiteInfo>("/api/site", TestJson.Options);
        Assert.Null(site!.PublicBaseUrl);
        Assert.Equal("http://localhost", site.Origin);
    }

    // VI: `docker run --env-file .env` chỉ truyền PUBLIC_BASE_URL. EN: `docker run --env-file .env` only passes PUBLIC_BASE_URL.
    [Theory]
    [InlineData("https://studyhub.example.top", "https://studyhub.example.top")]
    [InlineData(" https://StudyHub.Example.top/ ", "https://studyhub.example.top")]
    [InlineData("http://192.168.1.5:5080", "http://192.168.1.5:5080")]
    public async Task Env_file_name_sets_normalized_public_base_url(string value, string expected)
    {
        using var factory = new FactoryWith(("PUBLIC_BASE_URL", value));
        var site = await factory.CreateClient().GetFromJsonAsync<SiteInfo>("/api/site", TestJson.Options);
        Assert.Equal(expected, site!.PublicBaseUrl);
    }

    [Fact]
    public async Task Config_key_wins_over_env_file_name()
    {
        using var factory = new FactoryWith(("PublicSite:BaseUrl", "https://a.example.top"), ("PUBLIC_BASE_URL", "https://b.example.top"));
        var site = await factory.CreateClient().GetFromJsonAsync<SiteInfo>("/api/site", TestJson.Options);
        Assert.Equal("https://a.example.top", site!.PublicBaseUrl);
    }

    [Theory]
    [InlineData("studyhub.example.top")]
    [InlineData("ftp://studyhub.example.top")]
    [InlineData("https://studyhub.example.top/@someone")]
    public void Malformed_public_base_url_stops_start_up(string value)
    {
        using var factory = new FactoryWith(("PUBLIC_BASE_URL", value));
        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(PublicSite.InvalidMessage, ex.Message);
    }

    private static HttpRequestMessage ProxiedSiteRequest(string clientIp = "203.0.113.7")
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/site");
        req.Headers.Add("X-Forwarded-For", clientIp);
        req.Headers.Add("X-Forwarded-Proto", "https");
        req.Headers.Add("X-Forwarded-Host", "studyhub.example.top");
        return req;
    }

    [Theory]
    [InlineData("BEHIND_PROXY")]
    [InlineData("FORWARDEDHEADERS_ENABLED")] // VI: = biến ASPNETCORE_FORWARDEDHEADERS_ENABLED. EN: = the ASPNETCORE_FORWARDEDHEADERS_ENABLED env var.
    public async Task Behind_proxy_honors_forwarded_proto_and_host(string flag)
    {
        using var factory = new FactoryWith((flag, "true"));
        var res = await factory.CreateClient().SendAsync(ProxiedSiteRequest());
        var site = await res.Content.ReadFromJsonAsync<SiteInfo>(TestJson.Options);
        Assert.Equal("https://studyhub.example.top", site!.Origin);
    }

    [Fact]
    public async Task Forwarded_headers_are_ignored_when_not_behind_proxy()
    {
        using var factory = new StudyHubFactory();
        var res = await factory.CreateClient().SendAsync(ProxiedSiteRequest());
        var site = await res.Content.ReadFromJsonAsync<SiteInfo>(TestJson.Options);
        Assert.Equal("http://localhost", site!.Origin);
    }

    // VI: Sau proxy, mỗi người dùng có hạn mức đăng nhập riêng theo IP thật, không dùng chung IP của proxy.
    // EN: Behind the proxy each user gets their own login limit by real IP instead of sharing the proxy's IP.
    [Fact]
    public async Task Behind_proxy_rate_limits_by_real_client_ip()
    {
        using var factory = new FactoryWith(("BEHIND_PROXY", "true"), ("RateLimiting:AuthPermitsPerMinute", "1"));
        var client = factory.CreateClient();
        async Task<HttpStatusCode> Login(string ip)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest("nobody", "password123"))
            };
            req.Headers.Add("X-Forwarded-For", ip);
            return (await client.SendAsync(req)).StatusCode;
        }
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Login("203.0.113.1"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await Login("203.0.113.2"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Login("203.0.113.1"));
    }
}
