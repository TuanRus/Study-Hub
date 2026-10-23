namespace StudyHub.Api.Entities;

// VI: Trang hồ sơ công khai kiểu bio-link (guns.lol), xem được tại /@slug mà không cần đăng nhập.
//     Mỗi user có đúng một hồ sơ. Slug mặc định là username, đổi được nhưng phải duy nhất.
// EN: Public bio-link style profile (like guns.lol), viewable at /@slug without signing in.
//     Each user has exactly one. The slug defaults to the username and can be changed but must be unique.
public class Profile
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    // VI: Luôn lưu chữ thường để so sánh không phân biệt hoa thường.
    // EN: Always stored lower-case so lookups are case-insensitive.
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    // VI: Link http(s) hoặc ảnh tải lên dạng data:image (xem ProfileService.AvatarUrl).
    // EN: An http(s) link or an uploaded image as data:image (see ProfileService.AvatarUrl).
    public const int MaxAvatarLength = 200_000;
    public string? AvatarUrl { get; set; }
    public string Bio { get; set; } = string.Empty;
    public string? Location { get; set; }
    public List<string> Badges { get; set; } = [];

    // VI: aurora | sunset | ocean | night | custom (custom dùng BackgroundUrl).
    // EN: aurora | sunset | ocean | night | custom (custom uses BackgroundUrl).
    public string Background { get; set; } = "aurora";
    public string? BackgroundUrl { get; set; }
    public string Effect { get; set; } = "stars";
    public string AccentColor { get; set; } = "#8b7bff";
    public string? SongUrl { get; set; }
    public Dictionary<string, string> Links { get; set; } = [];

    public bool IsPublic { get; set; } = true;

    // VI: Admin ẩn hồ sơ (vi phạm). Chủ hồ sơ không tự bật lại được.
    // EN: Hidden by an admin (moderation). The owner cannot turn it back on.
    public bool IsSuspended { get; set; }
    public int ViewCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // VI: null = chưa sửa lần nào (import từ localStorage chỉ ghi đè khi còn null).
    // EN: null = never edited (a localStorage import only overwrites it while still null).
    public DateTime? UpdatedAt { get; set; }

    public List<ProfileView> Views { get; set; } = [];
}

// VI: Một lượt xem đã được đếm. Mỗi khách chỉ được đếm một lần mỗi ngày (UTC) cho mỗi hồ sơ.
//     Chỉ lưu hash, không lưu IP hay id khách thật.
// EN: A counted view. Each visitor counts once per profile per (UTC) day.
//     Only hashes are stored, never the raw IP or visitor id.
public class ProfileView
{
    public long Id { get; set; }
    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public DateOnly Day { get; set; }
    public string VisitorHash { get; set; } = string.Empty;
    public string IpHash { get; set; } = string.Empty;
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}
