using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

public class PomodoroService(AppDbContext db)
{
    private static readonly TimeSpan MaxSessionLength = TimeSpan.FromHours(4);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public async Task<PomodoroSettingsDto> GetSettingsAsync(Guid userId, CancellationToken ct) =>
        (await FindSettingsAsync(userId, ct)).ToDto();

    public async Task<PomodoroSettingsDto> UpdateSettingsAsync(Guid userId, PomodoroSettingsDto req, CancellationToken ct)
    {
        var s = await FindSettingsAsync(userId, ct);
        s.WorkMinutes = req.WorkMinutes;
        s.ShortBreakMinutes = req.ShortBreakMinutes;
        s.LongBreakMinutes = req.LongBreakMinutes;
        s.SessionsBeforeLongBreak = req.SessionsBeforeLongBreak;
        s.AutoStartNext = req.AutoStartNext;
        s.DailyGoalMinutes = req.DailyGoalMinutes;
        await db.SaveChangesAsync(ct);
        return s.ToDto();
    }

    public async Task<FocusSessionDto> LogSessionAsync(Guid userId, CreateFocusSessionRequest req, CancellationToken ct)
    {
        var started = req.StartedAt.UtcDateTime;
        var ended = req.EndedAt.UtcDateTime;
        if (ended <= started)
            throw new BadRequestException("EndedAt must be after StartedAt.");
        if (ended - started > MaxSessionLength)
            throw new BadRequestException("A session may last at most 4 hours.");
        if (ended > DateTime.UtcNow + ClockSkew)
            throw new BadRequestException("EndedAt cannot be in the future.");

        StudyTask? task = null;
        if (req.TaskId is Guid taskId)
            task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
                   ?? throw new NotFoundException("Task");

        var session = new FocusSession
        {
            UserId = userId,
            Kind = req.Kind,
            StartedAt = started,
            EndedAt = ended,
            DurationSeconds = (int)(ended - started).TotalSeconds,
            Completed = req.Completed,
            TaskId = task?.Id,
            Task = task
        };
        db.FocusSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.ToDto();
    }

    public async Task<List<FocusSessionDto>> ListSessionsAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, SessionKind? kind, CancellationToken ct)
    {
        var toUtc = to?.UtcDateTime ?? DateTime.UtcNow.AddMinutes(5);
        var fromUtc = from?.UtcDateTime ?? toUtc.AddDays(-7);

        var query = db.FocusSessions.AsNoTracking().Include(s => s.Task)
            .Where(s => s.UserId == userId && s.StartedAt >= fromUtc && s.StartedAt < toUtc);
        if (kind is not null)
            query = query.Where(s => s.Kind == kind);

        var list = await query.OrderByDescending(s => s.StartedAt).Take(1000).ToListAsync(ct);
        return list.Select(s => s.ToDto()).ToList();
    }

    public async Task DeleteSessionAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var deleted = await db.FocusSessions.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0)
            throw new NotFoundException("Focus session");
    }

    private async Task<PomodoroSettings> FindSettingsAsync(Guid userId, CancellationToken ct)
    {
        var settings = await db.PomodoroSettings.FindAsync([userId], ct);
        if (settings is null)
        {
            settings = new PomodoroSettings { UserId = userId };
            db.PomodoroSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }
        return settings;
    }
}
