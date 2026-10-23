using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

public class StatsService(AppDbContext db)
{
    public async Task<StatsSummaryDto> SummaryAsync(Guid userId, int days, string? timeZone, CancellationToken ct)
    {
        var zone = await db.ResolveTimeZoneAsync(userId, timeZone, ct);
        var today = TimeZones.Today(zone);
        var firstDay = today.AddDays(-(days - 1));
        var rangeStartUtc = TimeZones.StartOfDayUtc(firstDay, zone);

        // VI: Chỉ phiên Work mới tính là thời gian tập trung. Lấy 1 năm để tính chuỗi ngày.
        // EN: Only Work sessions count as focus time. Load one year to compute streaks.
        var yearAgoUtc = TimeZones.StartOfDayUtc(today.AddDays(-365), zone);
        var sessions = await db.FocusSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Kind == SessionKind.Work && s.StartedAt >= yearAgoUtc)
            .Select(s => new { s.StartedAt, s.DurationSeconds })
            .ToListAsync(ct);

        var byDay = sessions
            .GroupBy(s => TimeZones.LocalDate(s.StartedAt, zone))
            .ToDictionary(g => g.Key, g => new DailyStatDto(g.Key, g.Count(), g.Sum(s => s.DurationSeconds) / 60));

        var daily = Enumerable.Range(0, days)
            .Select(i => firstDay.AddDays(i))
            .Select(d => byDay.GetValueOrDefault(d) ?? new DailyStatDto(d, 0, 0))
            .ToList();

        var goal = await db.PomodoroSettings.Where(s => s.UserId == userId)
            .Select(s => (int?)s.DailyGoalMinutes).FirstOrDefaultAsync(ct) ?? 120;

        var now = DateTime.UtcNow;
        var todayDate = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var tasksCompleted = await db.Tasks.CountAsync(
            t => t.UserId == userId && t.IsCompleted && t.CompletedAt >= rangeStartUtc, ct);
        var tasksOverdue = await db.Tasks.CountAsync(
            t => t.UserId == userId && !t.IsCompleted && t.EndAt != null &&
                 (t.IsAllDay ? t.EndAt < todayDate : t.EndAt < now), ct);

        var (current, longest) = Streaks(byDay.Keys.ToHashSet(), today);

        return new StatsSummaryDto(
            zone.Id,
            daily[^1],
            goal,
            daily,
            daily.Sum(d => d.Sessions),
            daily.Sum(d => d.FocusMinutes),
            current,
            longest,
            tasksCompleted,
            tasksOverdue);
    }

    // VI: Chuỗi hiện tại vẫn giữ nếu hôm nay chưa học nhưng hôm qua có học.
    // EN: The current streak survives if today has no session yet but yesterday did.
    internal static (int Current, int Longest) Streaks(HashSet<DateOnly> activeDays, DateOnly today)
    {
        var current = 0;
        var day = activeDays.Contains(today) ? today : today.AddDays(-1);
        while (activeDays.Contains(day))
        {
            current++;
            day = day.AddDays(-1);
        }

        var longest = 0;
        foreach (var d in activeDays.Where(d => !activeDays.Contains(d.AddDays(-1))))
        {
            var length = 1;
            while (activeDays.Contains(d.AddDays(length))) length++;
            longest = Math.Max(longest, length);
        }
        return (current, longest);
    }
}
