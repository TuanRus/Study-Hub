namespace StudyHub.Api.Entities;

// VI: Mã OTP 6 số gửi qua email để đặt lại mật khẩu. Mỗi user chỉ có một mã còn hiệu lực.
//     Chỉ lưu hash (HMAC theo khoá JWT), không lưu mã gốc.
// EN: A 6-digit OTP sent by email to reset a password. Each user has at most one live code.
//     Only a hash (HMAC with the JWT key) is stored, never the code itself.
public class PasswordResetCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
}
