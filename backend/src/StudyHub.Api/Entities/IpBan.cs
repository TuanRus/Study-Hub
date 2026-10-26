namespace StudyHub.Api.Entities;

// VI: IP bị chặn: mọi request /api từ IP này bị từ chối (403). EN: A banned IP: every /api request from it gets 403.
public class IpBan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // VI: Dạng chuẩn hoá của IPAddress.ToString(). EN: Normalized IPAddress.ToString().
    public string Ip { get; set; } = string.Empty;
    public string? Reason { get; set; }
    // VI: Tài khoản liên quan lúc ban (nếu có), chỉ để tham khảo. EN: Related account at ban time (if any), for reference only.
    public string? Username { get; set; }
    public string BannedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
