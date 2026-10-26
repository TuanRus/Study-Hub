using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;

namespace StudyHub.Api.Tests;

public partial class PasswordResetTests(StudyHubFactory factory) : IClassFixture<StudyHubFactory>
{
    private static string NewName() => "r" + Guid.NewGuid().ToString("N")[..10];

    private async Task<(string Username, string Email, AuthResponse Auth)> RegisterAsync()
    {
        var name = NewName();
        var email = $"{name}@example.com";
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, "password123"));
        res.EnsureSuccessStatusCode();
        return (name, email, (await res.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!);
    }

    private string LastCode(string email) => SixDigits().Match(factory.Outbox.To(email).Last().TextBody).Value;

    [Fact]
    public async Task Code_by_email_resets_the_password_and_signs_out_other_devices()
    {
        var (name, email, auth) = await RegisterAsync();
        var client = factory.CreateClient();

        var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.NoContent, forgot.StatusCode);
        var code = LastCode(email);
        Assert.Equal(6, code.Length);

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(email, code, "brand-new-pass"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, "password123"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, "brand-new-pass"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken))).StatusCode);

        // VI: Mã chỉ dùng được một lần. EN: A code works only once.
        var again = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(email, code, "another-pass-1"));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Unknown_email_gets_the_same_answer_and_no_mail()
    {
        var before = factory.Outbox.Sent.Count;
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest($"{NewName()}@example.com"));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(before, factory.Outbox.Sent.Count);
    }

    [Fact]
    public async Task Wrong_codes_are_limited_and_expired_codes_fail()
    {
        var (_, email, _) = await RegisterAsync();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var code = LastCode(email);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password",
                new ResetPasswordRequest(email, wrong, "brand-new-pass"))).StatusCode);
        // VI: Sai 5 lần thì cả mã đúng cũng bị khoá. EN: After 5 wrong tries even the right code is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(email, code, "brand-new-pass"))).StatusCode);

        var (_, email2, _) = await RegisterAsync();
        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email2));
        var code2 = LastCode(email2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.PasswordResetCodes.SingleAsync(c => c.User.Email == email2);
            stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(email2, code2, "brand-new-pass"))).StatusCode);
    }

    [Fact]
    public async Task A_new_code_is_not_sent_again_within_a_minute()
    {
        var (_, email, _) = await RegisterAsync();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var second = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Single(factory.Outbox.To(email));
    }

    [Fact]
    public async Task Server_without_email_settings_says_so()
    {
        factory.Outbox.IsConfigured = false;
        try
        {
            var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password",
                new ForgotPasswordRequest($"{NewName()}@example.com"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        }
        finally
        {
            factory.Outbox.IsConfigured = true;
        }
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}
