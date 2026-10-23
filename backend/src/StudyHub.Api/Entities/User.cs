namespace StudyHub.Api.Entities;

public enum UserRole
{
    User = 0,
    // VI: Quản lý user thường và kiểm duyệt hồ sơ. EN: Manages regular users and moderates profiles.
    Admin = 1,
    // VI: Như Admin, cộng thêm quyền thêm/bỏ Admin. Chỉ tạo được qua cấu hình Admin:SuperAdmins.
    // EN: Like Admin, plus adding/removing Admins. Only created through the Admin:SuperAdmins setting.
    SuperAdmin = 2
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    // VI: Múi giờ IANA, dùng để tính "hôm nay", lịch và thống kê theo giờ địa phương.
    // EN: IANA time zone, used for "today", calendar and stats in local time.
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    // VI: Admin khóa tài khoản: không đăng nhập được, token hiện có bị từ chối ngay.
    // EN: Locked by an admin: cannot sign in, existing tokens are rejected right away.
    public bool IsDisabled { get; set; }

    public PomodoroSettings? PomodoroSettings { get; set; }
    public Profile? Profile { get; set; }
    public List<StudyTask> Tasks { get; set; } = [];
    public List<FocusSession> FocusSessions { get; set; } = [];
    public List<Playlist> Playlists { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
