namespace StudyHub.Api.Entities;

public enum SessionKind
{
    Work = 0,
    ShortBreak = 1,
    LongBreak = 2
}

// VI: Một phiên Pomodoro đã kết thúc (chạy hết hoặc bị skip).
// EN: A finished Pomodoro phase (ran to the end or was skipped).
public class FocusSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public SessionKind Kind { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public bool Completed { get; set; } = true;

    public Guid? TaskId { get; set; }
    public StudyTask? Task { get; set; }
}
