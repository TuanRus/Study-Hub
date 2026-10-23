using System.ComponentModel.DataAnnotations;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Dtos;

public record PomodoroSettingsDto(
    [Range(1, 180)] int WorkMinutes,
    [Range(1, 60)] int ShortBreakMinutes,
    [Range(1, 90)] int LongBreakMinutes,
    [Range(1, 12)] int SessionsBeforeLongBreak,
    bool AutoStartNext,
    [Range(0, 1440)] int DailyGoalMinutes);

public record CreateFocusSessionRequest(
    SessionKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool Completed = true,
    Guid? TaskId = null);

public record FocusSessionDto(
    Guid Id,
    SessionKind Kind,
    DateTime StartedAt,
    DateTime EndedAt,
    int DurationSeconds,
    bool Completed,
    Guid? TaskId,
    string? TaskTitle);

public record DailyStatDto(DateOnly Date, int Sessions, int FocusMinutes);

public record StatsSummaryDto(
    string TimeZone,
    DailyStatDto Today,
    int DailyGoalMinutes,
    IReadOnlyList<DailyStatDto> Days,
    int TotalSessions,
    int TotalFocusMinutes,
    int CurrentStreak,
    int LongestStreak,
    int TasksCompleted,
    int TasksOverdue);
