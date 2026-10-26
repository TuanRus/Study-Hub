using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

// VI: User báo cáo hồ sơ của người khác. Đủ số người báo cáo (ReportsToFlag) thì tài khoản tự bị gắn sao.
// EN: Users report someone else's profile. Enough different reporters (ReportsToFlag) star the account automatically.
public class ReportService(AppDbContext db, AbuseSettings settings)
{
    public const int MaxReportsPerDay = 10;

    public async Task CreateAsync(Guid reporterId, string slug, CreateReportRequest req, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var target = await db.Profiles.Where(p => p.Slug == normalized)
            .Select(p => new { p.UserId, p.User.FlagClearedAt }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Profile");
        if (target.UserId == reporterId)
            throw new BadRequestException("You cannot report yourself.");

        var dayAgo = DateTime.UtcNow.AddDays(-1);
        if (await db.UserReports.CountAsync(r => r.ReporterId == reporterId && r.CreatedAt >= dayAgo, ct) >= MaxReportsPerDay)
            throw new AppException(StatusCodes.Status429TooManyRequests, "Too many reports",
                $"You can send up to {MaxReportsPerDay} reports per day.");
        if (await db.UserReports.AnyAsync(r => r.ReporterId == reporterId && r.ReportedUserId == target.UserId &&
                                               r.Status == ReportStatus.Open, ct))
            throw new ConflictException("You already reported this account. An admin will look at it.");

        db.UserReports.Add(new UserReport
        {
            ReporterId = reporterId,
            ReportedUserId = target.UserId,
            Reason = req.Reason,
            Details = string.IsNullOrWhiteSpace(req.Details) ? null : req.Details.Trim()
        });
        await db.SaveChangesAsync(ct);

        // VI: Chỉ tính báo cáo còn mở, mỗi người một lần, sau lần gỡ sao gần nhất.
        // EN: Only open reports count, one per person, made after the last time the star was removed.
        var threshold = settings.Current.ReportsToFlag;
        if (threshold <= 0) return;
        var since = target.FlagClearedAt ?? DateTime.MinValue;
        var reporters = await db.UserReports
            .Where(r => r.ReportedUserId == target.UserId && r.Status == ReportStatus.Open && r.CreatedAt > since)
            .Select(r => r.ReporterId).Distinct().CountAsync(ct);
        if (reporters >= threshold)
            await AbuseSettings.StarAsync(db, target.UserId, $"Auto: reported by {reporters} users", ct);
    }
}
