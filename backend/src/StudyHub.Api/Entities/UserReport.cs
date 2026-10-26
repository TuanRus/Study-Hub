namespace StudyHub.Api.Entities;

public enum ReportReason
{
    Spam = 0,
    Abuse = 1,
    Inappropriate = 2,
    Impersonation = 3,
    Other = 4
}

public enum ReportStatus
{
    Open = 0,
    // VI: Admin đã xử lý (vd. khoá, dọn dữ liệu). EN: An admin acted on it (e.g. locked, cleaned up).
    Resolved = 1,
    // VI: Báo cáo không đúng. EN: Not a valid report.
    Dismissed = 2
}

// VI: Một user báo cáo hồ sơ/tài khoản của user khác. EN: One user reporting another user's profile/account.
public class UserReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReportedUserId { get; set; }
    public User ReportedUser { get; set; } = null!;
    public Guid ReporterId { get; set; }
    public User Reporter { get; set; } = null!;

    public ReportReason Reason { get; set; }
    public string? Details { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? HandledAt { get; set; }
    public string? HandledBy { get; set; }
}
