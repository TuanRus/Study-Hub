namespace StudyHub.Api.Dtos;

// VI: Đúng hình dạng dữ liệu frontend đang lưu trong localStorage
//     (studyhub_todos, studyhub_music, studyhub_focus_log).
// EN: Same shape as the frontend's localStorage data
//     (studyhub_todos, studyhub_music, studyhub_focus_log).
public record ImportLocalRequest(
    List<LocalTodo>? Todos,
    List<LocalMusic>? Music,
    Dictionary<string, LocalFocusDay>? FocusLog,
    LocalProfile? Profile = null);

// VI: deadline = "YYYY-MM-DD" (ngày địa phương), time = "HH:MM" hoặc "" (cả ngày), priority = "high" | "medium" | "low".
// EN: deadline = "YYYY-MM-DD" (local date), time = "HH:MM" or "" (all day), priority = "high" | "medium" | "low".
public record LocalTodo(
    long? Id,
    string? Text,
    bool Completed,
    string? Deadline = null,
    string? Time = null,
    string? Priority = null);

public record LocalMusic(string? Name, string? Url, string? Type = null);

public record LocalFocusDay(int Sessions, int Minutes);

// VI: Giống studyhub_profile ở frontend. Chỉ được nhập khi hồ sơ trên server chưa từng được sửa.
// EN: Same as the frontend's studyhub_profile. Only imported while the server profile has never been edited.
public record LocalProfile(
    string? Name,
    string? Handle,
    string? Avatar,
    string? Bio,
    string? Location,
    List<string>? Badges,
    string? Background,
    string? BackgroundUrl,
    string? Effect,
    string? Accent,
    string? Song,
    Dictionary<string, string>? Links);

public record ImportResultDto(
    int TasksImported,
    int TracksImported,
    int TracksSkipped,
    int FocusSessionsImported,
    bool ProfileImported = false);
