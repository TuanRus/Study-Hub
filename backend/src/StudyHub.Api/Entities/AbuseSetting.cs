namespace StudyHub.Api.Entities;

// VI: Ngưỡng chống spam do admin chỉnh, chỉ có một dòng (Id = 1). Chưa có dòng nào thì dùng cấu hình Abuse.
// EN: Anti-spam limits set by an admin; a single row (Id = 1). Without it the Abuse configuration is used.
public class AbuseSetting
{
    public int Id { get; set; } = 1;
    public int SuspiciousTasksPerDay { get; set; }
    public int MaxTasksPerDay { get; set; }
    public int ReportsToFlag { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
