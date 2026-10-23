using System.ComponentModel.DataAnnotations;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Dtos;

// VI: Với IsAllDay = true, StartAt/EndAt luôn là 00:00Z của ngày đó; client chỉ lấy phần ngày.
// EN: When IsAllDay = true, StartAt/EndAt are always 00:00Z of that date; clients should use the date part only.
public record TaskDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime? StartAt,
    DateTime? EndAt,
    bool IsAllDay,
    string? Color,
    TaskPriority Priority,
    bool IsCompleted,
    DateTime? CompletedAt,
    bool IsOverdue,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record SaveTaskRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Description = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null,
    bool IsAllDay = false,
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Color must look like #RRGGBB.")] string? Color = null,
    TaskPriority Priority = TaskPriority.Medium,
    bool IsCompleted = false);

public record ScheduleTaskRequest(
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    bool IsAllDay = false);

public record SetCompletionRequest(bool IsCompleted);

public enum TaskStatusFilter
{
    All,
    Active,
    Completed,
    Overdue
}

public record TaskQuery(
    TaskStatusFilter Status = TaskStatusFilter.All,
    string? Search = null,
    bool? Scheduled = null);

public record CalendarResponse(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    IReadOnlyList<TaskDto> Tasks);
