using System.ComponentModel.DataAnnotations;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Dtos;

public record ServerStatsDto(
    int Users,
    int Admins,
    int SuperAdmins,
    int DisabledUsers,
    int NewUsersLast7Days,
    int ActiveUsersLast7Days,
    int Tasks,
    int TasksCompleted,
    int FocusSessions,
    int FocusMinutes,
    int Playlists,
    int Tracks,
    int Profiles,
    int SuspendedProfiles,
    int ProfileViews,
    int ProfileViewsToday,
    long DatabaseBytes,
    DateTime StartedAt,
    long UptimeSeconds,
    string Environment,
    string Runtime,
    string Os,
    long MemoryBytes,
    bool DatabaseHealthy);

public record AdminUserDto(
    Guid Id,
    string Username,
    string Email,
    string? DisplayName,
    string Role,
    bool IsDisabled,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    int TaskCount,
    int FocusSessionCount,
    string? ProfileSlug,
    int ProfileViews,
    bool ProfileSuspended,
    bool IsFlagged,
    string? FlagReason,
    DateTime? FlaggedAt,
    // VI: Số task tạo trong 24 giờ qua; IsSuspicious khi vượt ngưỡng Abuse:SuspiciousTasksPerDay.
    // EN: Tasks created in the last 24 hours; IsSuspicious when past Abuse:SuspiciousTasksPerDay.
    int TasksLast24h,
    bool IsSuspicious,
    string? LastIp,
    string? RegisterIp,
    bool IpBanned,
    int OpenReports);

public record AdminUserPageDto(int Total, int Page, int PageSize, IReadOnlyList<AdminUserDto> Items);

// VI: Status: flagged (đánh dấu sao), suspicious (spam 24h), disabled (bị khoá). EN: See VI.
public enum AdminUserStatus { Flagged, Suspicious, Disabled, Reported }

public record AdminUserQuery(
    string? Search = null,
    UserRole? Role = null,
    AdminUserStatus? Status = null,
    [Range(1, 10000)] int Page = 1,
    [Range(1, 100)] int PageSize = 20);

public record SetUserStatusRequest(bool IsDisabled);

// VI: Chỉ nhận User hoặc Admin; SuperAdmin chỉ tạo qua cấu hình. EN: User or Admin only; SuperAdmin comes from config.
public record SetUserRoleRequest(UserRole Role);

public record ModerateProfileRequest(bool IsSuspended);

public record FlagUserRequest(bool IsFlagged, [StringLength(200)] string? Reason = null);

// VI: Xoá dữ liệu rác của một user. LastHours: chỉ xoá thứ tạo trong N giờ gần nhất (bỏ trống = tất cả).
// EN: Removes a user's junk data. LastHours: only what was created in the last N hours (empty = everything).
public record CleanupUserRequest(
    bool Tasks = false,
    bool FocusSessions = false,
    bool Tracks = false,
    bool Profile = false,
    [Range(1, 24 * 365)] int? LastHours = null);

public record CleanupResultDto(int Tasks, int FocusSessions, int Tracks, bool Profile);

public record IpBanDto(Guid Id, string Ip, string? Reason, string? Username, string BannedBy, DateTime CreatedAt);

public record BanIpRequest([Required, StringLength(45)] string Ip, [StringLength(200)] string? Reason = null);

// VI: Ban IP đăng ký và IP gần nhất của user; mặc định khoá luôn tài khoản. EN: Bans the user's sign-up and last IP; locks the account by default.
public record BanUserIpRequest([StringLength(200)] string? Reason = null, bool DisableAccount = true);

// VI: 0 = tắt. UpdatedBy/UpdatedAt rỗng khi đang dùng giá trị mặc định. EN: 0 = off. UpdatedBy/UpdatedAt are empty while on defaults.
public record AbuseSettingsDto(int SuspiciousTasksPerDay, int MaxTasksPerDay, int ReportsToFlag, string? UpdatedBy, DateTime? UpdatedAt);

public record SaveAbuseSettingsRequest(
    [Range(0, 10000)] int SuspiciousTasksPerDay,
    [Range(0, 10000)] int MaxTasksPerDay,
    [Range(0, 100)] int ReportsToFlag);

public record AdminReportDto(
    Guid Id,
    Guid ReportedUserId,
    string ReportedUsername,
    string? ReportedSlug,
    string ReporterUsername,
    string Reason,
    string? Details,
    string Status,
    DateTime CreatedAt,
    DateTime? HandledAt,
    string? HandledBy);

public record AdminReportPageDto(int Total, int Open, IReadOnlyList<AdminReportDto> Items);

public record HandleReportRequest(ReportStatus Status);
