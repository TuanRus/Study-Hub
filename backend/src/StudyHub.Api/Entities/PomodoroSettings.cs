namespace StudyHub.Api.Entities;

public class PomodoroSettings
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public int WorkMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int SessionsBeforeLongBreak { get; set; } = 4;
    public bool AutoStartNext { get; set; } = true;
    public int DailyGoalMinutes { get; set; } = 120;
}
