using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;
using StudyHub.Api.Services;

namespace StudyHub.Api.Tests;

public class AdminTests(StudyHubFactory factory) : IClassFixture<StudyHubFactory>
{
    private static string NewName() => "a" + Guid.NewGuid().ToString("N")[..10];

    private async Task<(HttpClient Client, AdminUserDto User)> NewUserAsync(HttpClient admin)
    {
        var name = NewName();
        var client = await factory.CreateUserClientAsync(name);
        var page = await admin.GetFromJsonAsync<AdminUserPageDto>($"/api/admin/users?search={name}", TestJson.Options);
        return (client, page!.Items.Single());
    }

    // VI: Super admin nâng một user mới lên Admin. EN: The super admin promotes a new user to Admin.
    private async Task<(HttpClient Client, AdminUserDto User)> NewAdminAsync(HttpClient super)
    {
        var (client, user) = await NewUserAsync(super);
        (await SetRole(super, user.Id, UserRole.Admin)).EnsureSuccessStatusCode();
        return (client, user with { Role = "Admin" });
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, TestJson.Options);

    private static Task<HttpResponseMessage> SetRole(HttpClient client, Guid id, UserRole role) =>
        Put(client, $"/api/admin/users/{id}/role", new SetUserRoleRequest(role));

    private static Task<HttpResponseMessage> SetDisabled(HttpClient client, Guid id, bool disabled) =>
        Put(client, $"/api/admin/users/{id}/status", new SetUserStatusRequest(disabled));

    [Fact]
    public async Task Admin_endpoints_need_an_admin_role()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/admin/stats")).StatusCode);
        var user = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Configured_username_becomes_super_admin_and_sees_server_stats()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var me = await super.GetFromJsonAsync<UserDto>("/api/auth/me", TestJson.Options);
        Assert.Equal("SuperAdmin", me!.Role);

        var stats = await super.GetFromJsonAsync<ServerStatsDto>("/api/admin/stats", TestJson.Options);
        Assert.True(stats!.Users >= 1);
        Assert.Equal(1, stats.SuperAdmins);
        Assert.True(stats.DatabaseBytes > 0);
        Assert.True(stats.DatabaseHealthy);
    }

    [Fact]
    public async Task Users_can_be_listed_and_searched()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var (_, user) = await NewUserAsync(super);
        Assert.Equal("User", user.Role);
        Assert.Equal(user.Username, user.ProfileSlug);

        var all = await super.GetFromJsonAsync<AdminUserPageDto>("/api/admin/users?pageSize=100", TestJson.Options);
        Assert.Contains(all!.Items, u => u.Id == user.Id);
    }

    [Fact]
    public async Task Super_admin_can_promote_and_demote_and_it_applies_at_once()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var (client, user) = await NewUserAsync(super);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/stats")).StatusCode);

        (await SetRole(super, user.Id, UserRole.Admin)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/stats")).StatusCode);

        (await SetRole(super, user.Id, UserRole.User)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/stats")).StatusCode);
    }

    [Fact]
    public async Task Nobody_can_grant_super_admin_through_the_api()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var (_, user) = await NewUserAsync(super);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetRole(super, user.Id, UserRole.SuperAdmin)).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_promote_or_demote_anyone()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var (admin, _) = await NewAdminAsync(super);
        var (_, otherAdmin) = await NewAdminAsync(super);
        var (_, user) = await NewUserAsync(super);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetRole(admin, user.Id, UserRole.Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetRole(admin, otherAdmin.Id, UserRole.User)).StatusCode);

        var page = await super.GetFromJsonAsync<AdminUserPageDto>($"/api/admin/users?search={user.Username}", TestJson.Options);
        Assert.Equal("User", page!.Items.Single().Role);
    }

    [Fact]
    public async Task Admin_manages_regular_users_only()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var me = await super.GetFromJsonAsync<UserDto>("/api/auth/me", TestJson.Options);
        var (admin, _) = await NewAdminAsync(super);
        var (_, otherAdmin) = await NewAdminAsync(super);
        var (_, user) = await NewUserAsync(super);

        Assert.Equal(HttpStatusCode.OK, (await SetDisabled(admin, user.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetDisabled(admin, otherAdmin.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetDisabled(admin, me!.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/admin/users/{otherAdmin.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/admin/users/{me.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(admin, $"/api/admin/profiles/{otherAdmin.Id}/moderation", new ModerateProfileRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, $"/api/admin/profiles/{user.Id}/moderation", new ModerateProfileRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/users/{user.Id}")).StatusCode);
    }

    [Fact]
    public async Task Super_admin_can_disable_admins_but_not_themselves()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var me = await super.GetFromJsonAsync<UserDto>("/api/auth/me", TestJson.Options);
        var (adminClient, admin) = await NewAdminAsync(super);

        Assert.Equal(HttpStatusCode.OK, (await SetDisabled(super, admin.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await adminClient.GetAsync("/api/admin/stats")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await SetDisabled(super, me!.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetRole(super, me.Id, UserRole.User)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.DeleteAsync($"/api/admin/users/{me.Id}")).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_blocks_their_token_and_login_at_once()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var (client, user) = await NewUserAsync(super);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks")).StatusCode);

        var res = await SetDisabled(super, user.Id, true);
        Assert.True((await res.Content.ReadFromJsonAsync<AdminUserDto>(TestJson.Options))!.IsDisabled);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tasks")).StatusCode);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, "Password123!"));
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        await SetDisabled(super, user.Id, false);
        login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, "Password123!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_data_and_profile()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var (client, user) = await NewUserAsync(super);
        await client.PostAsJsonAsync("/api/tasks", new SaveTaskRequest("Soon gone"), TestJson.Options);

        Assert.Equal(HttpStatusCode.NoContent, (await super.DeleteAsync($"/api/admin/users/{user.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/profiles/{user.Username}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tasks")).StatusCode);
    }

    [Fact]
    public async Task Suspended_profile_is_hidden_from_visitors()
    {
        var super = await factory.CreateSuperAdminClientAsync();
        var (owner, user) = await NewUserAsync(super);
        var url = $"/api/admin/profiles/{user.Id}/moderation";

        var res = await Put(super, url, new ModerateProfileRequest(true));
        Assert.True((await res.Content.ReadFromJsonAsync<AdminUserDto>(TestJson.Options))!.ProfileSuspended);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/profiles/{user.Username}")).StatusCode);
        var mine = await owner.GetFromJsonAsync<ProfileDto>("/api/profile", TestJson.Options);
        Assert.True(mine!.IsSuspended);

        await Put(super, url, new ModerateProfileRequest(false));
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync($"/api/profiles/{user.Username}")).StatusCode);
    }

    [Fact]
    public async Task Existing_accounts_listed_in_config_are_promoted_at_startup()
    {
        var name = NewName();
        await factory.CreateUserClientAsync(name);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:SuperAdmins:0"] = name.ToUpperInvariant() })
            .Build();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await new AdminBootstrap(config).PromoteConfiguredSuperAdminsAsync(db));
        Assert.Equal(UserRole.SuperAdmin, await db.Users.Where(u => u.Username == name).Select(u => u.Role).SingleAsync());
    }

    [Fact]
    public async Task Configured_super_admin_username_cannot_be_registered()
    {
        var client = factory.CreateClient();
        foreach (var name in new[] { StudyHubFactory.SuperAdminUsername, StudyHubFactory.SuperAdminUsername.ToUpperInvariant() })
        {
            var res = await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest(name, $"{Guid.NewGuid():N}@example.com", "Password123!"));
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        }
    }

    [Fact]
    public async Task Missing_super_admin_is_created_once_with_a_working_random_password()
    {
        var name = NewName();
        var bootstrap = new AdminBootstrap(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:SuperAdmins:0"] = name })
            .Build());

        // VI: Factory riêng để không làm lệch số SuperAdmin ở các test khác. EN: Own factory so other tests' SuperAdmin counts stay the same.
        using var own = new StudyHubFactory();
        using var scope = own.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var created = await bootstrap.CreateMissingSuperAdminsAsync(db, hasher, NullLogger.Instance);
        var account = Assert.Single(created);
        Assert.Equal(name, account.Username);
        Assert.True(account.Password.Length >= 20);

        var login = await own.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(name, account.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("SuperAdmin", (await login.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!.User.Role);

        Assert.Empty(await bootstrap.CreateMissingSuperAdminsAsync(db, hasher, NullLogger.Instance));
        Assert.Equal(1, await db.Users.CountAsync(u => u.Username == name));
    }

    [Fact]
    public async Task Configured_super_admin_exists_after_startup()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roles = await db.Users.Where(u => u.Username == StudyHubFactory.SuperAdminUsername).Select(u => u.Role).ToListAsync();
        Assert.Equal([UserRole.SuperAdmin], roles);
    }

    [Fact]
    public async Task Admin_page_is_served()
    {
        var res = await factory.CreateClient().GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
