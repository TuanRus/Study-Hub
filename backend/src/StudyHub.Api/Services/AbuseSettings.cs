using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

// VI: Ngưỡng chống spam đang dùng, giữ trong bộ nhớ. Nạp từ DB lúc khởi động; chưa có thì lấy cấu hình Abuse.
// EN: The anti-spam limits in use, kept in memory. Loaded from the DB at start-up; falls back to the Abuse configuration.
public class AbuseSettings(IOptions<AbuseOptions> defaults)
{
    private volatile AbuseSettingsDto? _current;

    public AbuseSettingsDto Current => _current ?? Defaults;

    private AbuseSettingsDto Defaults => new(
        defaults.Value.SuspiciousTasksPerDay, defaults.Value.MaxTasksPerDay, defaults.Value.ReportsToFlag, null, null);

    public async Task LoadAsync(AppDbContext db, CancellationToken ct = default)
    {
        var row = await db.AbuseSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        _current = row is null ? null : ToDto(row);
    }

    public async Task<AbuseSettingsDto> SaveAsync(AppDbContext db, SaveAbuseSettingsRequest req, string actor, CancellationToken ct)
    {
        if (req.MaxTasksPerDay > 0 && req.SuspiciousTasksPerDay > req.MaxTasksPerDay)
            throw new BadRequestException("The star threshold must not be above the daily task limit.");
        var row = await db.AbuseSettings.FirstOrDefaultAsync(ct);
        if (row is null) db.AbuseSettings.Add(row = new AbuseSetting());
        row.SuspiciousTasksPerDay = req.SuspiciousTasksPerDay;
        row.MaxTasksPerDay = req.MaxTasksPerDay;
        row.ReportsToFlag = req.ReportsToFlag;
        row.UpdatedBy = actor;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return _current = ToDto(row);
    }

    private static AbuseSettingsDto ToDto(AbuseSetting s) =>
        new(s.SuspiciousTasksPerDay, s.MaxTasksPerDay, s.ReportsToFlag, s.UpdatedBy, s.UpdatedAt);

    // VI: Tự gắn sao (chỉ user thường, chưa có sao). EN: Automatic star (regular users without a star only).
    public static Task StarAsync(AppDbContext db, Guid userId, string reason, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId && !u.IsFlagged && u.Role == UserRole.User)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsFlagged, true)
                .SetProperty(u => u.FlagReason, reason)
                .SetProperty(u => u.FlaggedAt, DateTime.UtcNow), ct);
}
