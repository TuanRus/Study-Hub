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

    // VI: Admin đánh dấu sao: tài khoản đáng ngờ / lạm dụng hệ thống, kèm lý do. Chỉ để theo dõi, không tự khoá.
    // EN: Starred by an admin: suspicious / abusive account, with a reason. Only a marker, it does not lock anything.
    public bool IsFlagged { get; set; }
    public string? FlagReason { get; set; }
    public DateTime? FlaggedAt { get; set; }
    // VI: Lúc admin gỡ sao. Task và báo cáo trước mốc này không còn tính để tự gắn sao lại.
    // EN: When an admin removed the star. Tasks and reports before this no longer count towards an automatic star.
    public DateTime? FlagClearedAt { get; set; }

    // VI: IP lúc đăng ký và lần đăng nhập gần nhất, để admin ban IP. EN: IP at sign-up and at the last sign-in, so admins can ban it.
    public string? RegisterIp { get; set; }
    public string? LastIp { get; set; }

    public PomodoroSettings? PomodoroSettings { get; set; }
    public Profile? Profile { get; set; }
    public List<StudyTask> Tasks { get; set; } = [];
    public List<FocusSession> FocusSessions { get; set; } = [];
    public List<Playlist> Playlists { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
