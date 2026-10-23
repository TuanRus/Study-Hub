using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

public class AdminService(AppDbContext db, IWebHostEnvironment env)
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
                                     (u.Profile != null && EF.Functions.Like(u.Profile.Slug, term)));
        }

        var total = await users.CountAsync(ct);
        var items = await users
            .OrderByDescending(u => u.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new AdminUserDto(
                u.Id, u.Username, u.Email, u.DisplayName, u.Role.ToString(), u.IsDisabled, u.CreatedAt, u.LastLoginAt,
                u.Tasks.Count, u.FocusSessions.Count(s => s.Kind == SessionKind.Work),
                u.Profile != null ? u.Profile.Slug : null,
                u.Profile != null ? u.Profile.ViewCount : 0,
                u.Profile != null && u.Profile.IsSuspended))
            .ToListAsync(ct);
        return new AdminUserPageDto(total, query.Page, query.PageSize, items);
    }

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
        await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new AdminUserDto(
                u.Id, u.Username, u.Email, u.DisplayName, u.Role.ToString(), u.IsDisabled, u.CreatedAt, u.LastLoginAt,
                u.Tasks.Count, u.FocusSessions.Count(s => s.Kind == SessionKind.Work),
                u.Profile != null ? u.Profile.Slug : null,
                u.Profile != null ? u.Profile.ViewCount : 0,
                u.Profile != null && u.Profile.IsSuspended))
            .FirstAsync(ct);

    private long DatabaseSize()
    {
        var source = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrEmpty(source) || source == ":memory:") return 0;
        var path = Path.IsPathRooted(source) ? source : Path.Combine(Directory.GetCurrentDirectory(), source);
        return new[] { path, path + "-wal", path + "-shm" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
    }
}
