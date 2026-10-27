using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Tests;

public partial class PasswordPolicyTests(StudyHubFactory factory) : IClassFixture<StudyHubFactory>
{
    private const string Strong = "Password123!";

    private static string NewName() => "p" + Guid.NewGuid().ToString("N")[..10];

    [Theory]
    [InlineData("Pass1!")]        // VI: quá ngắn. EN: too short.
    [InlineData("password123!")]  // VI: thiếu chữ hoa. EN: no uppercase.
    [InlineData("PASSWORD123!")]  // VI: thiếu chữ thường. EN: no lowercase.
    [InlineData("Password!!!")]   // VI: thiếu chữ số. EN: no digit.
    [InlineData("Password1234")]  // VI: thiếu ký tự đặc biệt. EN: no special character.
    public async Task Register_rejects_weak_passwords(string password)
    {
        var name = NewName();
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(name, $"{name}@example.com", password));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Password must", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Policy_endpoint_reports_the_default_minimum_of_8()
    {
        var policy = await factory.CreateClient().GetFromJsonAsync<PasswordPolicyDto>("/api/auth/password-policy", TestJson.Options);
        Assert.Equal(8, policy!.MinLength);
        Assert.Equal(PasswordPolicy.MaxLength, policy.MaxLength);
    }

    [Fact]
    public async Task Change_password_requires_a_strong_new_password()
    {
        var client = await factory.CreateUserClientAsync();
        var weak = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Strong, "weakpassword"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Strong, "N3w-Password"));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Reset_with_a_weak_password_keeps_the_code_usable()
    {
        var name = NewName();
        var email = $"{name}@example.com";
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, Strong))).EnsureSuccessStatusCode();
        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var code = SixDigits().Match(factory.Outbox.To(email).Last().TextBody).Value;

        for (var i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password",
                new ResetPasswordRequest(email, code, "weakpassword"))).StatusCode);

        var ok = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(email, code, "Res3t-Password"));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Existing_account_with_an_old_weak_password_still_logs_in()
    {
        var client = await factory.CreateUserClientAsync();
        var me = (await client.GetFromJsonAsync<UserDto>("/api/auth/me", TestJson.Options))!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            var user = await db.Users.SingleAsync(u => u.Id == me.Id);
            user.PasswordHash = hasher.HashPassword(user, "password123");
            await db.SaveChangesAsync();
        }

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(me.Username, "password123"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public void Minimum_length_comes_from_configuration()
    {
        var policy = new PasswordPolicy(Options.Create(new PasswordPolicyOptions { PasswordMinLength = 12 }));
        Assert.Equal(12, policy.MinLength);
        Assert.NotEmpty(policy.Problems("Abcdefg12!x"));
        Assert.Empty(policy.Problems("Abcdefg12!xy"));
        Assert.Throws<BadRequestException>(() => policy.EnsureStrong("Abcdefg12!x"));
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}
