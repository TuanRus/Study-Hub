using StudyHub.Api.Entities;

namespace StudyHub.Api.Dtos;

public static class Mapping
{
    public static UserDto ToDto(this User u) =>
        new(u.Id, u.Username, u.Email, u.DisplayName, u.TimeZone, u.CreatedAt, u.Role.ToString());

    public static TaskDto ToDto(this StudyTask t, bool isOverdue) =>
        new(t.Id, t.Title, t.Description, t.StartAt, t.EndAt, t.IsAllDay, t.Color, t.Priority,
            t.IsCompleted, t.CompletedAt, isOverdue, t.CreatedAt, t.UpdatedAt);

    public static PomodoroSettingsDto ToDto(this PomodoroSettings s) =>
        new(s.WorkMinutes, s.ShortBreakMinutes, s.LongBreakMinutes, s.SessionsBeforeLongBreak,
            s.AutoStartNext, s.DailyGoalMinutes);

    public static FocusSessionDto ToDto(this FocusSession s) =>
        new(s.Id, s.Kind, s.StartedAt, s.EndedAt, s.DurationSeconds, s.Completed, s.TaskId, s.Task?.Title);

    public static TrackDto ToDto(this Track t) =>
        new(t.Id, t.Title, t.SourceUrl, t.EmbedUrl, t.Provider, t.Position, t.AddedAt);
}
