namespace StudyHub.Api.Common;

public static class TimeZones
{
    public static bool IsValid(string? id) => id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    public static TimeZoneInfo Find(string? id) =>
        id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : TimeZoneInfo.Utc;

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, tz));

    // VI: Thời điểm UTC ứng với 00:00 giờ địa phương của ngày đó.
    // EN: The UTC instant of local midnight on that date.
    public static DateTime StartOfDayUtc(DateOnly date, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), tz);

    public static DateOnly Today(TimeZoneInfo tz) => LocalDate(DateTime.UtcNow, tz);
}
