using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Tests;

// VI: Ngưỡng thấp để test nhanh. EN: Low limits so the tests stay fast.
public class AbuseFactory : StudyHubFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Abuse:SuspiciousTasksPerDay", "3");
        builder.UseSetting("Abuse:MaxTasksPerDay", "5");
    }
}

public class AbuseTests(AbuseFactory factory) : IClassFixture<AbuseFactory>
{
    private static string NewName() => "b" + Guid.NewGuid().ToString("N")[..10];

    // VI: Mỗi lần gọi một IP công khai khác (dải TEST-NET-3). EN: A different public IP per call (TEST-NET-3 range).
    private static int _ip = 1;
    private static string NewIp() => $"203.0.113.{Interlocked.Increment(ref _ip)}";

    private async Task<(HttpClient Client, string Name)> RegisterFromAsync(string ip)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestIpStartupFilter.Header, ip);
        var name = NewName();
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, $"{name}@example.com", "Password123!"));
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, name);
    }

    private static async Task<AdminUserDto> FindAsync(HttpClient admin, string name) =>
        (await admin.GetFromJsonAsync<AdminUserPageDto>($"/api/admin/users?search={name}", TestJson.Options))!.Items.Single();

    private static Task<HttpResponseMessage> AddTask(HttpClient client, string title = "spam") =>
        client.PostAsJsonAsync("/api/tasks", new SaveTaskRequest(title), TestJson.Options);

    [Fact]
    public async Task Sign_up_ip_is_recorded_and_searchable()
    {
        var ip = NewIp();
        var (_, name) = await RegisterFromAsync(ip);
        var admin = await factory.CreateSuperAdminClientAsync();
        var user = await FindAsync(admin, name);
        Assert.Equal(ip, user.RegisterIp);
        Assert.Equal(ip, user.LastIp);

        var byIp = await admin.GetFromJsonAsync<AdminUserPageDto>($"/api/admin/users?search={ip}", TestJson.Options);
        Assert.Equal(name, byIp!.Items.Single().Username);
    }

    [Fact]
    public async Task Task_spam_stars_the_account_and_is_capped()
    {
        var (client, name) = await RegisterFromAsync(NewIp());
        var admin = await factory.CreateSuperAdminClientAsync();

        for (var i = 0; i < 2; i++) (await AddTask(client)).EnsureSuccessStatusCode();
        Assert.False((await FindAsync(admin, name)).IsFlagged);

        for (var i = 0; i < 3; i++) (await AddTask(client)).EnsureSuccessStatusCode();
        var user = await FindAsync(admin, name);
        Assert.True(user.IsFlagged);
        Assert.True(user.IsSuspicious);
        Assert.Equal(5, user.TasksLast24h);
        Assert.StartsWith("Auto:", user.FlagReason);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await AddTask(client)).StatusCode);

        var flagged = await admin.GetFromJsonAsync<AdminUserPageDto>("/api/admin/users?status=flagged&pageSize=100", TestJson.Options);
        Assert.Contains(flagged!.Items, u => u.Username == name);
        var suspicious = await admin.GetFromJsonAsync<AdminUserPageDto>("/api/admin/users?status=suspicious&pageSize=100", TestJson.Options);
        Assert.Contains(suspicious!.Items, u => u.Username == name);
    }

    [Fact]
    public async Task Admin_can_flag_unflag_and_filter_by_role()
    {
        var (_, name) = await RegisterFromAsync(NewIp());
        var admin = await factory.CreateSuperAdminClientAsync();
        var user = await FindAsync(admin, name);

        var res = await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}/flag", new FlagUserRequest(true, "junk tasks"), TestJson.Options);
        var flagged = (await res.Content.ReadFromJsonAsync<AdminUserDto>(TestJson.Options))!;
        Assert.True(flagged.IsFlagged);
        Assert.Equal("junk tasks", flagged.FlagReason);

        res = await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}/flag", new FlagUserRequest(false), TestJson.Options);
        Assert.False((await res.Content.ReadFromJsonAsync<AdminUserDto>(TestJson.Options))!.IsFlagged);

        var supers = await admin.GetFromJsonAsync<AdminUserPageDto>("/api/admin/users?role=SuperAdmin", TestJson.Options);
        Assert.All(supers!.Items, u => Assert.Equal("SuperAdmin", u.Role));
        Assert.Equal(StudyHubFactory.SuperAdminUsername, supers.Items.Single().Username);
        var regular = await admin.GetFromJsonAsync<AdminUserPageDto>("/api/admin/users?role=User&pageSize=100", TestJson.Options);
        Assert.All(regular!.Items, u => Assert.Equal("User", u.Role));
    }

    [Fact]
    public async Task Cleanup_removes_junk_but_keeps_the_account()
    {
        var (client, name) = await RegisterFromAsync(NewIp());
        for (var i = 0; i < 3; i++) (await AddTask(client)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/profile", new SaveProfileRequest(name, "Spammer", Bio: "buy now buy now"), TestJson.Options))
            .EnsureSuccessStatusCode();
        var admin = await factory.CreateSuperAdminClientAsync();
        var user = await FindAsync(admin, name);

        var res = await admin.PostAsJsonAsync($"/api/admin/users/{user.Id}/cleanup",
            new CleanupUserRequest(Tasks: true, Profile: true, LastHours: 24), TestJson.Options);
        var result = (await res.Content.ReadFromJsonAsync<CleanupResultDto>(TestJson.Options))!;
        Assert.Equal(3, result.Tasks);
        Assert.True(result.Profile);

        Assert.Empty((await client.GetFromJsonAsync<List<TaskDto>>("/api/tasks", TestJson.Options))!);
        var profile = await client.GetFromJsonAsync<ProfileDto>("/api/profile", TestJson.Options);
        Assert.Equal(name, profile!.DisplayName);
        Assert.Equal("", profile.Bio);
    }

    [Fact]
    public async Task Banned_ip_gets_403_until_unbanned()
    {
        var ip = NewIp();
        var (client, name) = await RegisterFromAsync(ip);
        var admin = await factory.CreateSuperAdminClientAsync();
        var user = await FindAsync(admin, name);

        var res = await admin.PostAsJsonAsync($"/api/admin/users/{user.Id}/ban-ip", new BanUserIpRequest("spam"), TestJson.Options);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var bans = (await res.Content.ReadFromJsonAsync<List<IpBanDto>>(TestJson.Options))!;
        Assert.Equal(ip, bans.Single().Ip);
        Assert.Equal(name, bans.Single().Username);

        var after = await FindAsync(admin, name);
        Assert.True(after.IpBanned);
        Assert.True(after.IsDisabled);

        // VI: Cả tài khoản mới từ IP đó cũng bị chặn. EN: Even a new account from that IP is blocked.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/tasks")).StatusCode);
        var fresh = factory.CreateClient();
        fresh.DefaultRequestHeaders.Add(TestIpStartupFilter.Header, ip);
        var reg = await fresh.PostAsJsonAsync("/api/auth/register", new RegisterRequest(NewName(), $"{NewName()}@example.com", "Password123!"));
        Assert.Equal(HttpStatusCode.Forbidden, reg.StatusCode);

        // VI: IP khác vẫn dùng bình thường. EN: Other IPs still work.
        await RegisterFromAsync(NewIp());

        (await admin.DeleteAsync($"/api/admin/bans/{bans.Single().Id}")).EnsureSuccessStatusCode();
        Assert.DoesNotContain(await admin.GetFromJsonAsync<List<IpBanDto>>("/api/admin/bans", TestJson.Options) ?? [], b => b.Ip == ip);
        reg = await fresh.PostAsJsonAsync("/api/auth/register", new RegisterRequest(NewName(), $"{NewName()}@example.com", "Password123!"));
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
    }

    [Fact]
    public async Task Private_own_and_admin_ips_cannot_be_banned()
    {
        var admin = await factory.CreateSuperAdminClientAsync();
        foreach (var ip in new[] { "127.0.0.1", "192.168.1.20", "172.18.0.5", "10.0.0.1", "::1" })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PostAsJsonAsync("/api/admin/bans", new BanIpRequest(ip), TestJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/admin/bans", new BanIpRequest("not-an-ip"), TestJson.Options)).StatusCode);

        // VI: Ban tay một IP công khai thì được, ban lại thì 409. EN: A public IP can be banned by hand; again gives 409.
        var ip2 = NewIp();
        var res = await admin.PostAsJsonAsync("/api/admin/bans", new BanIpRequest(ip2, "bot"), TestJson.Options);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/admin/bans", new BanIpRequest(ip2), TestJson.Options)).StatusCode);

        // VI: Không tự ban IP của mình. EN: Cannot ban your own IP.
        var mine = NewIp();
        admin.DefaultRequestHeaders.Add(TestIpStartupFilter.Header, mine);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/admin/bans", new BanIpRequest(mine), TestJson.Options)).StatusCode);
    }

    [Fact]
    public async Task Ban_user_ip_without_recorded_ip_is_rejected()
    {
        var admin = await factory.CreateSuperAdminClientAsync();
        var name = NewName();
        await factory.CreateUserClientAsync(name);
        var user = await FindAsync(admin, name);
        Assert.Null(user.LastIp);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/admin/users/{user.Id}/ban-ip", new BanUserIpRequest(), TestJson.Options)).StatusCode);
    }

    private static Task<HttpResponseMessage> Report(HttpClient client, string slug, ReportReason reason = ReportReason.Spam) =>
        client.PostAsJsonAsync($"/api/profiles/{slug}/report", new CreateReportRequest(reason, "junk"), TestJson.Options);

    [Fact]
    public async Task Unstarring_is_not_undone_by_old_tasks()
    {
        var (client, name) = await RegisterFromAsync(NewIp());
        var admin = await factory.CreateSuperAdminClientAsync();
        for (var i = 0; i < 3; i++) (await AddTask(client)).EnsureSuccessStatusCode();
        var user = await FindAsync(admin, name);
        Assert.True(user.IsFlagged);

        (await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}/flag", new FlagUserRequest(false), TestJson.Options)).EnsureSuccessStatusCode();
        // VI: Một task nữa không gắn sao lại; phải đủ 3 task mới sau khi gỡ. EN: One more task does not re-star; it takes 3 new ones.
        (await AddTask(client)).EnsureSuccessStatusCode();
        Assert.False((await FindAsync(admin, name)).IsFlagged);
        (await AddTask(client)).EnsureSuccessStatusCode();
        Assert.False((await FindAsync(admin, name)).IsFlagged);
    }

    [Fact]
    public async Task Admins_can_change_the_star_threshold()
    {
        var admin = await factory.CreateSuperAdminClientAsync();
        var current = await admin.GetFromJsonAsync<AbuseSettingsDto>("/api/admin/settings/abuse", TestJson.Options);
        Assert.Equal(3, current!.SuspiciousTasksPerDay);
        Assert.Equal(5, current.MaxTasksPerDay);

        // VI: Admin thường xem được nhưng không đổi được. EN: A regular admin can read but not change them.
        var (adminClient, adminName) = await RegisterFromAsync(NewIp());
        var adminUser = await FindAsync(admin, adminName);
        (await admin.PutAsJsonAsync($"/api/admin/users/{adminUser.Id}/role", new SetUserRoleRequest(UserRole.Admin), TestJson.Options))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/api/admin/settings/abuse")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PutAsJsonAsync("/api/admin/settings/abuse",
            new SaveAbuseSettingsRequest(1, 5, 1), TestJson.Options)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/settings/abuse",
            new SaveAbuseSettingsRequest(10, 5, 3), TestJson.Options)).StatusCode);

        try
        {
            var res = await admin.PutAsJsonAsync("/api/admin/settings/abuse", new SaveAbuseSettingsRequest(2, 5, 3), TestJson.Options);
            var saved = (await res.Content.ReadFromJsonAsync<AbuseSettingsDto>(TestJson.Options))!;
            Assert.Equal(2, saved.SuspiciousTasksPerDay);
            Assert.Equal(StudyHubFactory.SuperAdminUsername, saved.UpdatedBy);

            var (client, name) = await RegisterFromAsync(NewIp());
            (await AddTask(client)).EnsureSuccessStatusCode();
            Assert.False((await FindAsync(admin, name)).IsFlagged);
            (await AddTask(client)).EnsureSuccessStatusCode();
            Assert.True((await FindAsync(admin, name)).IsFlagged);
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/admin/settings/abuse", new SaveAbuseSettingsRequest(3, 5, 3), TestJson.Options);
        }
    }

    [Fact]
    public async Task Reports_from_users_reach_admins_and_star_after_enough_people()
    {
        var (_, target) = await RegisterFromAsync(NewIp());
        var admin = await factory.CreateSuperAdminClientAsync();
        var reporters = new List<HttpClient>();
        for (var i = 0; i < 3; i++) reporters.Add((await RegisterFromAsync(NewIp())).Client);

        // VI: Không tự báo cáo mình, không báo cáo trùng, không báo cáo hồ sơ không tồn tại.
        // EN: No reporting yourself, no duplicate reports, no reports on missing profiles.
        var (self, selfName) = await RegisterFromAsync(NewIp());
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(self, selfName)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Report(self, "no-such-profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Report(factory.CreateClient(), target)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Report(reporters[0], target)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Report(reporters[0], target)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Report(reporters[1], target, ReportReason.Abuse)).StatusCode);

        var user = await FindAsync(admin, target);
        Assert.Equal(2, user.OpenReports);
        Assert.False(user.IsFlagged);
        var reported = await admin.GetFromJsonAsync<AdminUserPageDto>("/api/admin/users?status=reported&pageSize=100", TestJson.Options);
        Assert.Contains(reported!.Items, u => u.Username == target);

        Assert.Equal(HttpStatusCode.NoContent, (await Report(reporters[2], target)).StatusCode);
        user = await FindAsync(admin, target);
        Assert.True(user.IsFlagged);
        Assert.Equal("Auto: reported by 3 users", user.FlagReason);

        var page = await admin.GetFromJsonAsync<AdminReportPageDto>("/api/admin/reports?status=Open", TestJson.Options);
        var mine = page!.Items.Where(r => r.ReportedUsername == target).ToList();
        Assert.Equal(3, mine.Count);
        Assert.Contains(mine, r => r.Reason == "Abuse");

        (await admin.PutAsJsonAsync($"/api/admin/reports/{mine[0].Id}", new HandleReportRequest(ReportStatus.Dismissed), TestJson.Options))
            .EnsureSuccessStatusCode();
        Assert.Equal(2, (await FindAsync(admin, target)).OpenReports);
        var dismissed = await admin.GetFromJsonAsync<AdminReportPageDto>("/api/admin/reports?status=Dismissed", TestJson.Options);
        Assert.Equal(StudyHubFactory.SuperAdminUsername, dismissed!.Items.Single(r => r.Id == mine[0].Id).HandledBy);

        // VI: Gỡ sao rồi thì báo cáo cũ không gắn lại; một báo cáo mới cũng chưa đủ.
        // EN: After unstarring, old reports do not re-star; one new report is not enough.
        (await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}/flag", new FlagUserRequest(false), TestJson.Options)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await Report((await RegisterFromAsync(NewIp())).Client, target)).StatusCode);
        Assert.False((await FindAsync(admin, target)).IsFlagged);
    }
}
