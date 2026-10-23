using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

public class TaskService(AppDbContext db)
{
    private const int MaxCalendarDays = 366;

    public async Task<List<TaskDto>> ListAsync(Guid userId, TaskQuery query, string? timeZone, CancellationToken ct)
    {
        var clock = await ClockAsync(userId, timeZone, ct);
        var tasks = db.Tasks.AsNoTracking().Where(t => t.UserId == userId);

        tasks = query.Status switch
        {
            TaskStatusFilter.Active => tasks.Where(t => !t.IsCompleted),
            TaskStatusFilter.Completed => tasks.Where(t => t.IsCompleted),
            TaskStatusFilter.Overdue => tasks.Where(t => !t.IsCompleted && t.EndAt != null &&
                (t.IsAllDay ? t.EndAt < clock.TodayDate : t.EndAt < clock.Now)),
            _ => tasks
        };
        if (query.Scheduled is bool scheduled)
            tasks = scheduled ? tasks.Where(t => t.EndAt != null) : tasks.Where(t => t.EndAt == null);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            tasks = tasks.Where(t => EF.Functions.Like(t.Title, term) ||
                                     (t.Description != null && EF.Functions.Like(t.Description, term)));
        }

        var list = await tasks
            .OrderBy(t => t.IsCompleted)
            .ThenBy(t => t.EndAt == null)
            .ThenBy(t => t.EndAt)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return list.Select(t => t.ToDto(clock.IsOverdue(t))).ToList();
    }

    public async Task<CalendarResponse> CalendarAsync(
        Guid userId, DateOnly? from, DateOnly? to, string? timeZone, CancellationToken ct)
    {
        var clock = await ClockAsync(userId, timeZone, ct);
        var today = TimeZones.Today(clock.Zone);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? start.AddMonths(1).AddDays(-1);
        if (end < start)
            throw new BadRequestException("'to' must not be before 'from'.");
        if (end.DayNumber - start.DayNumber >= MaxCalendarDays)
            throw new BadRequestException($"The range may span at most {MaxCalendarDays} days.");

        // VI: Việc có giờ cụ thể so theo UTC; việc cả ngày so theo "ngày lịch" (00:00Z).
        // EN: Timed tasks are compared in UTC; all-day tasks by calendar date (00:00Z).
        var utcFrom = TimeZones.StartOfDayUtc(start, clock.Zone);
        var utcTo = TimeZones.StartOfDayUtc(end.AddDays(1), clock.Zone);
        var dateFrom = AsCalendarDate(start);
        var dateTo = AsCalendarDate(end.AddDays(1));

        var list = await db.Tasks.AsNoTracking()
            .Where(t => t.UserId == userId && t.EndAt != null)
            .Where(t => t.IsAllDay
                ? (t.StartAt ?? t.EndAt) < dateTo && t.EndAt >= dateFrom
                : (t.StartAt ?? t.EndAt) < utcTo && t.EndAt >= utcFrom)
            .OrderBy(t => t.StartAt ?? t.EndAt)
            .ToListAsync(ct);

        return new CalendarResponse(start, end, clock.Zone.Id, list.Select(t => t.ToDto(clock.IsOverdue(t))).ToList());
    }

    public async Task<List<TaskDto>> UpcomingAsync(Guid userId, int days, string? timeZone, CancellationToken ct)
    {
        var clock = await ClockAsync(userId, timeZone, ct);
        var untilUtc = clock.Now.AddDays(days);
        var untilDate = clock.TodayDate.AddDays(days + 1);

        var list = await db.Tasks.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsCompleted && t.EndAt != null)
            .Where(t => t.IsAllDay ? t.EndAt < untilDate : t.EndAt <= untilUtc)
            .OrderBy(t => t.EndAt)
            .Take(100)
            .ToListAsync(ct);
        return list.Select(t => t.ToDto(clock.IsOverdue(t))).ToList();
    }

    public async Task<TaskDto> GetAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var task = await FindAsync(userId, id, ct);
        return task.ToDto((await ClockAsync(userId, null, ct)).IsOverdue(task));
    }

    public async Task<TaskDto> CreateAsync(Guid userId, SaveTaskRequest req, CancellationToken ct)
    {
        var task = new StudyTask { UserId = userId };
        Apply(task, req);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task.ToDto((await ClockAsync(userId, null, ct)).IsOverdue(task));
    }

    public async Task<TaskDto> UpdateAsync(Guid userId, Guid id, SaveTaskRequest req, CancellationToken ct)
    {
        var task = await FindAsync(userId, id, ct);
        Apply(task, req);
        await db.SaveChangesAsync(ct);
        return task.ToDto((await ClockAsync(userId, null, ct)).IsOverdue(task));
    }

    public async Task<TaskDto> ScheduleAsync(Guid userId, Guid id, ScheduleTaskRequest req, CancellationToken ct)
    {
        var task = await FindAsync(userId, id, ct);
        SetSchedule(task, req.StartAt, req.EndAt, req.IsAllDay);
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return task.ToDto((await ClockAsync(userId, null, ct)).IsOverdue(task));
    }

    public async Task<TaskDto> SetCompletionAsync(Guid userId, Guid id, bool isCompleted, CancellationToken ct)
    {
        var task = await FindAsync(userId, id, ct);
        SetCompleted(task, isCompleted);
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return task.ToDto((await ClockAsync(userId, null, ct)).IsOverdue(task));
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var task = await FindAsync(userId, id, ct);
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
    }

    private async Task<StudyTask> FindAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct)
        ?? throw new NotFoundException("Task");

    private static void Apply(StudyTask task, SaveTaskRequest req)
    {
        task.Title = req.Title.Trim();
        task.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        task.Color = req.Color?.ToLowerInvariant();
        task.Priority = req.Priority;
        SetSchedule(task, req.StartAt, req.EndAt, req.IsAllDay);
        SetCompleted(task, req.IsCompleted);
        task.UpdatedAt = DateTime.UtcNow;
    }

    private static void SetCompleted(StudyTask task, bool isCompleted)
    {
        if (task.IsCompleted == isCompleted) return;
        task.IsCompleted = isCompleted;
        task.CompletedAt = isCompleted ? DateTime.UtcNow : null;
    }

    private static void SetSchedule(StudyTask task, DateTimeOffset? startAt, DateTimeOffset? endAt, bool isAllDay)
    {
        if (startAt is not null && endAt is null)
            throw new BadRequestException("EndAt is required when StartAt is set.");
        if (isAllDay && endAt is null)
            throw new BadRequestException("An all-day task needs at least EndAt.");

        DateTime? start, end;
        if (isAllDay)
        {
            // VI: Lấy đúng ngày mà người dùng thấy trên lịch, bỏ qua giờ và độ lệch múi giờ.
            // EN: Keep the date the user saw on the calendar, ignoring time and offset.
            start = startAt is null ? null : AsCalendarDate(DateOnly.FromDateTime(startAt.Value.DateTime));
            end = AsCalendarDate(DateOnly.FromDateTime(endAt!.Value.DateTime));
        }
        else
        {
            start = startAt?.UtcDateTime;
            end = endAt?.UtcDateTime;
        }
        if (start > end)
            throw new BadRequestException("EndAt must not be before StartAt.");

        task.StartAt = start;
        task.EndAt = end;
        task.IsAllDay = isAllDay;
    }

    private static DateTime AsCalendarDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private async Task<Clock> ClockAsync(Guid userId, string? timeZone, CancellationToken ct)
    {
        var zone = await db.ResolveTimeZoneAsync(userId, timeZone, ct);
        return new Clock(zone, DateTime.UtcNow, AsCalendarDate(TimeZones.Today(zone)));
    }

    private sealed record Clock(TimeZoneInfo Zone, DateTime Now, DateTime TodayDate)
    {
        public bool IsOverdue(StudyTask t) =>
            !t.IsCompleted && t.EndAt is { } end && (t.IsAllDay ? end < TodayDate : end < Now);
    }
}
