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
    bool ProfileSuspended);

public record AdminUserPageDto(int Total, int Page, int PageSize, IReadOnlyList<AdminUserDto> Items);

public record AdminUserQuery(
    string? Search = null,
    [Range(1, 10000)] int Page = 1,
    [Range(1, 100)] int PageSize = 20);

public record SetUserStatusRequest(bool IsDisabled);

// VI: Chỉ nhận User hoặc Admin; SuperAdmin chỉ tạo qua cấu hình. EN: User or Admin only; SuperAdmin comes from config.
public record SetUserRoleRequest(UserRole Role);

public record ModerateProfileRequest(bool IsSuspended);
