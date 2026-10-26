using System.Diagnostics;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

public class AdminService(AppDbContext db, IWebHostEnvironment env, IpBanList bans, AbuseSettings abuse)
{
    private static readonly DateTime StartedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public async Task<ServerStatsDto> StatsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var today = DateOnly.FromDateTime(now);

        bool healthy;
        try { healthy = await db.Database.CanConnectAsync(ct); } catch { healthy = false; }

        return new ServerStatsDto(
            await db.Users.CountAsync(ct),
            await db.Users.CountAsync(u => u.Role == UserRole.Admin, ct),
            await db.Users.CountAsync(u => u.Role == UserRole.SuperAdmin, ct),
            await db.Users.CountAsync(u => u.IsDisabled, ct),
            await db.Users.CountAsync(u => u.CreatedAt >= weekAgo, ct),
            await db.Users.CountAsync(u => u.LastLoginAt >= weekAgo, ct),
            await db.Tasks.CountAsync(ct),
            await db.Tasks.CountAsync(t => t.IsCompleted, ct),
            await db.FocusSessions.CountAsync(s => s.Kind == SessionKind.Work, ct),
            (int)(await db.FocusSessions.Where(s => s.Kind == SessionKind.Work).SumAsync(s => (long)s.DurationSeconds, ct) / 60),
            await db.Playlists.CountAsync(ct),
            await db.Tracks.CountAsync(ct),
            await db.Profiles.CountAsync(ct),
            await db.Profiles.CountAsync(p => p.IsSuspended, ct),
            await db.Profiles.SumAsync(p => p.ViewCount, ct),
            await db.ProfileViews.CountAsync(v => v.Day == today, ct),
            DatabaseSize(),
            StartedAt,
            (long)(now - StartedAt).TotalSeconds,
            env.EnvironmentName,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Environment.WorkingSet,
            healthy);
    }

    public async Task<AdminUserPageDto> ListUsersAsync(AdminUserQuery query, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            users = users.Where(u => EF.Functions.Like(u.Username, term) || EF.Functions.Like(u.Email, term) ||
                                     (u.DisplayName != null && EF.Functions.Like(u.DisplayName, term)) ||
                                     (u.Profile != null && EF.Functions.Like(u.Profile.Slug, term)) ||
                                     (u.LastIp != null && EF.Functions.Like(u.LastIp, term)) ||
                                     (u.RegisterIp != null && EF.Functions.Like(u.RegisterIp, term)));
        }
        if (query.Role is UserRole role)
            users = users.Where(u => u.Role == role);

        var dayAgo = DateTime.UtcNow.AddDays(-1);
        var threshold = SuspiciousThreshold;
        users = query.Status switch
        {
            AdminUserStatus.Flagged => users.Where(u => u.IsFlagged),
            AdminUserStatus.Suspicious => users.Where(u => u.Tasks.Count(t => t.CreatedAt >= dayAgo) >= threshold),
            AdminUserStatus.Disabled => users.Where(u => u.IsDisabled),
            AdminUserStatus.Reported => users.Where(u => db.UserReports.Any(r => r.ReportedUserId == u.Id && r.Status == ReportStatus.Open)),
            _ => users
        };

        var total = await users.CountAsync(ct);
        var items = await users
            .OrderByDescending(u => u.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToDto(db, dayAgo, threshold))
            .ToListAsync(ct);
        return new AdminUserPageDto(total, query.Page, query.PageSize, items.Select(WithBan).ToList());
    }

    // VI: Ngưỡng tắt (0) thì không ai bị coi là đáng ngờ. EN: A disabled (0) threshold means nobody is suspicious.
    private int SuspiciousThreshold => abuse.Current.SuspiciousTasksPerDay > 0 ? abuse.Current.SuspiciousTasksPerDay : int.MaxValue;

    private static Expression<Func<User, AdminUserDto>> ToDto(AppDbContext db, DateTime dayAgo, int threshold) => u => new AdminUserDto(
        u.Id, u.Username, u.Email, u.DisplayName, u.Role.ToString(), u.IsDisabled, u.CreatedAt, u.LastLoginAt,
        u.Tasks.Count, u.FocusSessions.Count(s => s.Kind == SessionKind.Work),
        u.Profile != null ? u.Profile.Slug : null,
        u.Profile != null ? u.Profile.ViewCount : 0,
        u.Profile != null && u.Profile.IsSuspended,
        u.IsFlagged, u.FlagReason, u.FlaggedAt,
        u.Tasks.Count(t => t.CreatedAt >= dayAgo),
        u.Tasks.Count(t => t.CreatedAt >= dayAgo) >= threshold,
        u.LastIp, u.RegisterIp, false,
        db.UserReports.Count(r => r.ReportedUserId == u.Id && r.Status == ReportStatus.Open));

    private AdminUserDto WithBan(AdminUserDto u) => u with { IpBanned = bans.IsBanned(u.LastIp) || bans.IsBanned(u.RegisterIp) };

    // VI: Quy tắc chung: chỉ được tác động lên người có quyền thấp hơn mình, và không bao giờ lên chính mình.
    //     Admin -> chỉ User; SuperAdmin -> User và Admin.
    // EN: General rule: you may only act on accounts ranked below you, never on yourself.
    //     Admin -> Users only; SuperAdmin -> Users and Admins.
    private async Task<User> FindTargetAsync(Guid actorId, Guid userId, CancellationToken ct)
    {
        if (actorId == userId)
            throw new BadRequestException("You cannot change your own account here.");
        var actorRole = await db.Users.Where(u => u.Id == actorId).Select(u => u.Role).FirstAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User");
        if (user.Role >= actorRole)
            throw new AppException(StatusCodes.Status403Forbidden, "Forbidden",
                actorRole == UserRole.Admin
                    ? "Admins can only manage regular users."
                    : "Super admins are managed through server configuration.");
        return user;
    }

    public async Task<AdminUserDto> SetStatusAsync(Guid actorId, Guid userId, SetUserStatusRequest req, CancellationToken ct)
    {
        var user = await FindTargetAsync(actorId, userId, ct);
        user.IsDisabled = req.IsDisabled;
        // VI: Khóa thì đăng xuất mọi thiết bị. EN: Disabling signs the user out everywhere.
        if (req.IsDisabled)
            await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
        await db.SaveChangesAsync(ct);
        return await GetUserAsync(userId, ct);
    }

    // VI: Chỉ SuperAdmin gọi được (policy ở controller). EN: SuperAdmin only (policy on the controller).
    public async Task<AdminUserDto> SetRoleAsync(Guid actorId, Guid userId, SetUserRoleRequest req, CancellationToken ct)
    {
        if (req.Role is not (UserRole.User or UserRole.Admin))
            throw new BadRequestException("Role must be User or Admin. Super admins are set in server configuration.");
        var user = await FindTargetAsync(actorId, userId, ct);
        user.Role = req.Role;
        await db.SaveChangesAsync(ct);
        return await GetUserAsync(userId, ct);
    }

    public async Task DeleteUserAsync(Guid actorId, Guid userId, CancellationToken ct)
    {
        var user = await FindTargetAsync(actorId, userId, ct);
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AdminUserDto> ModerateProfileAsync(Guid actorId, Guid userId, ModerateProfileRequest req, CancellationToken ct)
    {
        await FindTargetAsync(actorId, userId, ct);
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? throw new NotFoundException("Profile");
        profile.IsSuspended = req.IsSuspended;
        await db.SaveChangesAsync(ct);
        return await GetUserAsync(userId, ct);
    }

    private async Task<AdminUserDto> GetUserAsync(Guid userId, CancellationToken ct) =>
        WithBan(await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(ToDto(db, DateTime.UtcNow.AddDays(-1), SuspiciousThreshold))
            .FirstAsync(ct));

    public async Task<AdminUserDto> FlagAsync(Guid actorId, Guid userId, FlagUserRequest req, CancellationToken ct)
    {
        var user = await FindTargetAsync(actorId, userId, ct);
        user.IsFlagged = req.IsFlagged;
        user.FlagReason = req.IsFlagged ? (string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim()) : null;
        user.FlaggedAt = req.IsFlagged ? DateTime.UtcNow : null;
        // VI: Gỡ sao: task và báo cáo cũ không gắn sao lại nữa. EN: Unstarring: old tasks and reports no longer re-star the account.
        if (!req.IsFlagged) user.FlagClearedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetUserAsync(userId, ct);
    }

    // VI: Xoá dữ liệu rác (task spam, phiên focus giả, nhạc...) mà không xoá tài khoản.
    // EN: Removes junk data (spam tasks, fake focus sessions, tracks...) without deleting the account.
    public async Task<CleanupResultDto> CleanupAsync(Guid actorId, Guid userId, CleanupUserRequest req, CancellationToken ct)
    {
        await FindTargetAsync(actorId, userId, ct);
        var since = req.LastHours is int hours ? DateTime.UtcNow.AddHours(-hours) : DateTime.MinValue;

        var tasks = req.Tasks
            ? await db.Tasks.Where(t => t.UserId == userId && t.CreatedAt >= since).ExecuteDeleteAsync(ct) : 0;
        var sessions = req.FocusSessions
            ? await db.FocusSessions.Where(f => f.UserId == userId && f.EndedAt >= since).ExecuteDeleteAsync(ct) : 0;
        var tracks = req.Tracks
            ? await db.Tracks.Where(t => t.Playlist.UserId == userId && t.AddedAt >= since).ExecuteDeleteAsync(ct) : 0;

        var profileCleared = false;
        if (req.Profile && await db.Profiles.Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == userId, ct) is { } profile)
        {
            profile.DisplayName = profile.User.Username;
            profile.Bio = string.Empty;
            profile.Location = null;
            profile.Role = null;
            profile.AvatarUrl = null;
            profile.BackgroundUrl = null;
            profile.SongUrl = null;
            profile.Badges = [];
            profile.Links = [];
            profile.User.DisplayName = profile.User.Username;
            await db.SaveChangesAsync(ct);
            profileCleared = true;
        }
        return new CleanupResultDto(tasks, sessions, tracks, profileCleared);
    }

    // ---------- Anti-spam settings ----------
    public AbuseSettingsDto GetAbuseSettings() => abuse.Current;

    public async Task<AbuseSettingsDto> SaveAbuseSettingsAsync(Guid actorId, SaveAbuseSettingsRequest req, CancellationToken ct)
    {
        var actor = await db.Users.Where(u => u.Id == actorId).Select(u => u.Username).FirstAsync(ct);
        return await abuse.SaveAsync(db, req, actor, ct);
    }

    // ---------- Reports from users ----------
    public async Task<AdminReportPageDto> ListReportsAsync(ReportStatus? status, CancellationToken ct)
    {
        var reports = db.UserReports.AsNoTracking();
        if (status is ReportStatus s) reports = reports.Where(r => r.Status == s);
        var total = await reports.CountAsync(ct);
        var open = await db.UserReports.CountAsync(r => r.Status == ReportStatus.Open, ct);
        var items = await reports.OrderByDescending(r => r.CreatedAt).Take(100)
            .Select(r => new AdminReportDto(
                r.Id, r.ReportedUserId, r.ReportedUser.Username,
                r.ReportedUser.Profile != null ? r.ReportedUser.Profile.Slug : null,
                r.Reporter.Username, r.Reason.ToString(), r.Details, r.Status.ToString(),
                r.CreatedAt, r.HandledAt, r.HandledBy))
            .ToListAsync(ct);
        return new AdminReportPageDto(total, open, items);
    }

    public async Task HandleReportAsync(Guid actorId, Guid reportId, HandleReportRequest req, CancellationToken ct)
    {
        var report = await db.UserReports.FirstOrDefaultAsync(r => r.Id == reportId, ct) ?? throw new NotFoundException("Report");
        report.Status = req.Status;
        report.HandledAt = req.Status == ReportStatus.Open ? null : DateTime.UtcNow;
        report.HandledBy = req.Status == ReportStatus.Open ? null
            : await db.Users.Where(u => u.Id == actorId).Select(u => u.Username).FirstAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<IpBanDto>> ListBansAsync(CancellationToken ct) =>
        await db.IpBans.AsNoTracking().OrderByDescending(b => b.CreatedAt)
            .Select(b => new IpBanDto(b.Id, b.Ip, b.Reason, b.Username, b.BannedBy, b.CreatedAt))
            .ToListAsync(ct);

    public async Task<IpBanDto> BanIpAsync(Guid actorId, string? actorIp, BanIpRequest req, CancellationToken ct)
    {
        if (!IPAddress.TryParse(req.Ip.Trim(), out var parsed))
            throw new BadRequestException("Not a valid IP address.");
        var ip = IpBanList.Normalize(parsed);
        await CheckCanBanAsync(actorId, actorIp, ip, ct);
        var ban = await AddBanAsync(actorId, ip, req.Reason, null, ct) ?? throw new ConflictException("This IP is already banned.");
        await bans.ReloadAsync(db, ct);
        return ban;
    }

    // VI: Ban IP đăng ký và IP gần nhất của user (bỏ qua IP nội bộ), có thể khoá luôn tài khoản.
    // EN: Bans the user's sign-up and last IP (private ones are skipped) and can lock the account too.
    public async Task<List<IpBanDto>> BanUserIpAsync(Guid actorId, string? actorIp, Guid userId, BanUserIpRequest req, CancellationToken ct)
    {
        var user = await FindTargetAsync(actorId, userId, ct);
        var ips = new[] { user.LastIp, user.RegisterIp }
            .Where(ip => ip is not null && IPAddress.TryParse(ip, out var a) && !IpBanList.IsPrivate(a))
            .Distinct().ToList();
        if (ips.Count == 0)
            throw new BadRequestException(user.LastIp is null && user.RegisterIp is null
                ? "No IP has been recorded for this user yet (they have not signed in since IP logging started)."
                : $"Only private network IPs were recorded ({user.LastIp}). Set BEHIND_PROXY=true so the server sees real client IPs.");

        var added = new List<IpBanDto>();
        foreach (var ip in ips)
        {
            await CheckCanBanAsync(actorId, actorIp, ip!, ct);
            if (await AddBanAsync(actorId, ip!, req.Reason, user.Username, ct) is { } ban) added.Add(ban);
        }
        if (req.DisableAccount && !user.IsDisabled)
            await SetStatusAsync(actorId, userId, new SetUserStatusRequest(true), ct);
        await bans.ReloadAsync(db, ct);
        return added;
    }

    public async Task UnbanIpAsync(Guid banId, CancellationToken ct)
    {
        var removed = await db.IpBans.Where(b => b.Id == banId).ExecuteDeleteAsync(ct);
        if (removed == 0) throw new NotFoundException("Ban");
        await bans.ReloadAsync(db, ct);
    }

    // VI: Không ban IP nội bộ, IP của chính mình, hay IP mà admin cùng cấp/cao hơn đang dùng.
    // EN: No private IPs, not your own IP, and not an IP used by an admin of your rank or higher.
    private async Task CheckCanBanAsync(Guid actorId, string? actorIp, string ip, CancellationToken ct)
    {
        if (IpBanList.IsPrivate(IPAddress.Parse(ip)))
            throw new BadRequestException(
                $"{ip} is a private/local address (LAN, Docker or proxy). Banning it could block everyone. " +
                "If every user shows this IP, set BEHIND_PROXY=true so the server sees real client IPs.");
        if (ip == actorIp)
            throw new BadRequestException("You cannot ban your own IP address.");
        var actorRole = await db.Users.Where(u => u.Id == actorId).Select(u => u.Role).FirstAsync(ct);
        // VI: Role lưu dạng chuỗi nên không so sánh >= trong SQL được. EN: Roles are stored as strings, so no >= in SQL.
        var protectedRoles = Enum.GetValues<UserRole>().Where(r => r >= actorRole).ToList();
        if (await db.Users.AnyAsync(u => protectedRoles.Contains(u.Role) && u.Id != actorId && (u.LastIp == ip || u.RegisterIp == ip), ct))
            throw new AppException(StatusCodes.Status403Forbidden, "Forbidden", "This IP is used by an admin account.");
    }

    private async Task<IpBanDto?> AddBanAsync(Guid actorId, string ip, string? reason, string? username, CancellationToken ct)
    {
        if (await db.IpBans.AnyAsync(b => b.Ip == ip, ct)) return null;
        var actor = await db.Users.Where(u => u.Id == actorId).Select(u => u.Username).FirstAsync(ct);
        var ban = new IpBan
        {
            Ip = ip,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Username = username,
            BannedBy = actor
        };
        db.IpBans.Add(ban);
        await db.SaveChangesAsync(ct);
        return new IpBanDto(ban.Id, ban.Ip, ban.Reason, ban.Username, ban.BannedBy, ban.CreatedAt);
    }

    private long DatabaseSize()
    {
        var source = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrEmpty(source) || source == ":memory:") return 0;
        var path = Path.IsPathRooted(source) ? source : Path.Combine(Directory.GetCurrentDirectory(), source);
        return new[] { path, path + "-wal", path + "-shm" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
    }
}
