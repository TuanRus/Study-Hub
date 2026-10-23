using System.Net;
using System.Net.Http.Json;
using StudyHub.Api.Dtos;

namespace StudyHub.Api.Tests;

public class AuthTests(StudyHubFactory factory) : IClassFixture<StudyHubFactory>
{
    [Fact]
    public async Task Register_then_login_returns_tokens_and_profile()
    {
        var client = factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("alice", "Alice@Example.com", "password123", "Alice"));
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("alice@example.com", "password123"));
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options);
        Assert.Equal("alice", auth!.User.Username);
        Assert.Equal("alice@example.com", auth.User.Email);
        Assert.False(string.IsNullOrEmpty(auth.AccessToken));
    }

    [Fact]
    public async Task Duplicate_username_is_rejected()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("bob", "bob@example.com", "password123"));
        var again = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("BOB", "bob2@example.com", "password123"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_returns_401()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("carol", "carol@example.com", "password123"));
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("carol", "nope-nope"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reuse_revokes_all_sessions()
    {
        var client = factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("dave", "dave@example.com", "password123"));
        var first = (await reg.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));
        var second = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var afterTheft = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(second.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterTheft.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var res = await factory.CreateClient().GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
