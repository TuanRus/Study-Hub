namespace StudyHub.Api.Entities;

public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2
}

// VI: Một việc / deadline, hiển thị trên lịch kiểu Notion Calendar.
//     - Không có StartAt và EndAt: to-do thường, không nằm trên lịch.
//     - Chỉ có EndAt: deadline (hạn chót tại một thời điểm).
//     - Có cả hai: một khối thời gian trên lịch (VD 14:00 - 16:00).
//     - IsAllDay = true: StartAt/EndAt là "ngày lịch" (giờ 00:00, không đổi theo múi giờ),
//       EndAt là ngày cuối cùng (tính cả ngày đó).
// EN: A to-do / deadline shown on a Notion Calendar-like calendar.
//     - No StartAt and no EndAt: plain to-do, not on the calendar.
//     - EndAt only: a deadline at one point in time.
//     - Both: a time block on the calendar (e.g. 14:00 - 16:00).
//     - IsAllDay = true: StartAt/EndAt are calendar dates (00:00, time-zone independent),
//       EndAt is the last day (inclusive).
public class StudyTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsAllDay { get; set; }

    // VI: Màu hiển thị trên lịch, dạng #RRGGBB.
    // EN: Calendar display color, as #RRGGBB.
    public string? Color { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
