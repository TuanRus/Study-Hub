using System.ComponentModel.DataAnnotations;

namespace StudyHub.Api.Dtos;

// VI: Tên trường khớp với hồ sơ ở frontend (js/modules/profile.js), chỉ khác: handle -> slug, accent -> accentColor,
//     avatar -> avatarUrl, song -> songUrl, name -> displayName.
// EN: Fields mirror the frontend profile (js/modules/profile.js), except: handle -> slug, accent -> accentColor,
//     avatar -> avatarUrl, song -> songUrl, name -> displayName.
public record SaveProfileRequest(
    [Required, StringLength(32, MinimumLength = 3),
     RegularExpression("^[a-zA-Z0-9_.]+$", ErrorMessage = "Slug may only contain letters, digits, '_' and '.'.")]
    string Slug,
    [Required, StringLength(40, MinimumLength = 1)] string DisplayName,
    [StringLength(500)] string? AvatarUrl = null,
    [StringLength(400)] string? Bio = null,
    [StringLength(40)] string? Location = null,
    List<string>? Badges = null,
    [StringLength(20)] string? Background = null,
    [StringLength(500)] string? BackgroundUrl = null,
    [StringLength(20)] string? Effect = null,
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "AccentColor must look like #RRGGBB.")] string? AccentColor = null,
    [StringLength(500)] string? SongUrl = null,
    Dictionary<string, string>? Links = null,
    bool IsPublic = true);

// VI: Số liệu học tập hiện trên thẻ hồ sơ. EN: Study numbers shown on the profile card.
public record ProfileStatsDto(
    int TotalFocusMinutes,
    int WeekFocusMinutes,
    int TasksCompleted,
    int CurrentStreak,
    DateTime? LastFocusAt);

// VI: Hồ sơ của chính mình (có cả IsPublic, UpdatedAt). EN: Your own profile (includes IsPublic, UpdatedAt).
public record ProfileDto(
    string Slug,
    string DisplayName,
    string? AvatarUrl,
    string Bio,
    string? Location,
    IReadOnlyList<string> Badges,
    string Background,
    string? BackgroundUrl,
    string Effect,
    string AccentColor,
    string? SongUrl,
    IReadOnlyDictionary<string, string> Links,
    bool IsPublic,
    bool IsSuspended,
    int ViewCount,
    ProfileStatsDto Stats,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// VI: Thứ khách thấy tại /@slug. EN: What visitors see at /@slug.
public record PublicProfileDto(
    string Slug,
    string DisplayName,
    string? AvatarUrl,
    string Bio,
    string? Location,
    IReadOnlyList<string> Badges,
    string Background,
    string? BackgroundUrl,
    string Effect,
    string AccentColor,
    string? SongUrl,
    IReadOnlyDictionary<string, string> Links,
    int ViewCount,
    ProfileStatsDto Stats,
    bool IsOwner);

public record SlugAvailabilityDto(string Slug, bool Available);
