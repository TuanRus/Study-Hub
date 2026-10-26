using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StudyHub.Api.Auth;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

// VI: Thông tin khách đang xem hồ sơ, dùng để chống đếm trùng.
// EN: Who is viewing a profile, used to avoid double counting.
public record ProfileVisitor(string? VisitorId, string? Ip, string? UserAgent, Guid? UserId);

public partial class ProfileService(AppDbContext db, IOptions<JwtOptions> jwt)
{
    public static readonly HashSet<string> Badges = [
        "student", "focus", "night", "coder", "music", "earlybird", "bookworm", "math", "science", "language",
        "writer", "artist", "gamer", "athlete", "coffee", "streak", "goal", "exam", "team", "traveler",
    ];
    public static readonly HashSet<string> Backgrounds = ["aurora", "sunset", "ocean", "night", "custom"];
    public static readonly HashSet<string> Effects = ["stars", "snow", "none"];
    public static readonly HashSet<string> LinkTypes =
        ["facebook", "discord", "github", "linkedin", "instagram", "tiktok", "youtube", "spotify", "website"];

    // VI: Một IP (vd. cả lớp dùng chung wifi) được tính tối đa chừng này lượt mỗi ngày cho một hồ sơ.
    // EN: One IP (e.g. a whole class on shared wifi) counts at most this many views per profile per day.
    internal const int MaxViewsPerIpPerDay = 20;

    public async Task<ProfileDto> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var profile = await EnsureAsync(userId, ct);
        return ToDto(profile, await StatsAsync(userId, ct));
    }

    public async Task<ProfileDto> SaveAsync(Guid userId, SaveProfileRequest req, CancellationToken ct)
    {
        var profile = await EnsureAsync(userId, ct);
        var slug = req.Slug.Trim().ToLowerInvariant();
        if (slug != profile.Slug && await db.Profiles.AnyAsync(p => p.Slug == slug && p.UserId != userId, ct))
            throw new ConflictException("This profile link is already taken.");

        var badges = (req.Badges ?? []).Select(b => b.Trim().ToLowerInvariant()).Distinct().ToList();
        if (badges.FirstOrDefault(b => !Badges.Contains(b)) is { } badBadge)
            throw new BadRequestException($"Unknown badge '{badBadge}'.");

        var background = (req.Background ?? "aurora").Trim().ToLowerInvariant();
        if (!Backgrounds.Contains(background))
            throw new BadRequestException($"Unknown background '{background}'.");
        var effect = (req.Effect ?? "stars").Trim().ToLowerInvariant();
        if (!Effects.Contains(effect))
            throw new BadRequestException($"Unknown effect '{effect}'.");

        var links = new Dictionary<string, string>();
        foreach (var (key, value) in req.Links ?? [])
        {
            var type = key.Trim().ToLowerInvariant();
            if (!LinkTypes.Contains(type))
                throw new BadRequestException($"Unknown link type '{key}'.");
            if (string.IsNullOrWhiteSpace(value)) continue;
            links[type] = HttpUrl(value, $"Link '{key}'");
        }

        var song = OptionalUrl(req.SongUrl, "SongUrl");
        if (song is not null)
            MusicLinkResolver.Resolve(song);

        profile.Slug = slug;
        profile.DisplayName = req.DisplayName.Trim();
        profile.AvatarUrl = AvatarUrl(req.AvatarUrl);
        profile.Bio = req.Bio?.Trim() ?? string.Empty;
        profile.Location = string.IsNullOrWhiteSpace(req.Location) ? null : req.Location.Trim();
        profile.Role = string.IsNullOrWhiteSpace(req.Role) ? null : req.Role.Trim();
        profile.Badges = badges;
        profile.Background = background;
        profile.BackgroundUrl = OptionalUrl(req.BackgroundUrl, "BackgroundUrl");
        profile.Effect = effect;
        profile.AccentColor = (req.AccentColor ?? "#8b7bff").ToLowerInvariant();
        profile.SongUrl = song;
        profile.Links = links;
        profile.IsPublic = req.IsPublic;
        profile.UpdatedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // VI: Hai người cùng lấy một slug cùng lúc. EN: Two people grabbed the same slug at once.
            throw new ConflictException("This profile link is already taken.");
        }
        return ToDto(profile, await StatsAsync(userId, ct));
    }

    public async Task<SlugAvailabilityDto> CheckSlugAsync(Guid userId, string slug, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var available = SlugPattern().IsMatch(normalized) &&
                        !await db.Profiles.AnyAsync(p => p.Slug == normalized && p.UserId != userId, ct);
        return new SlugAvailabilityDto(normalized, available);
    }

    // VI: Đọc hồ sơ công khai. Nếu countView = true thì đếm lượt xem (mỗi khách một lần/ngày, chủ hồ sơ không tính).
    // EN: Reads a public profile. With countView = true it records a view (once per visitor per day, never the owner).
    public async Task<PublicProfileDto> GetPublicAsync(string slug, ProfileVisitor visitor, bool countView, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Slug == normalized, ct);
        var isOwner = profile is not null && visitor.UserId == profile.UserId;
        if (profile is null || ((!profile.IsPublic || profile.IsSuspended) && !isOwner))
            throw new NotFoundException("Profile");

        if (countView && !isOwner && await TryCountViewAsync(profile, visitor, ct))
        {
            await db.Profiles.Where(p => p.UserId == profile.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1), ct);
            profile.ViewCount++;
        }

        var stats = await StatsAsync(profile.UserId, ct);
        return new PublicProfileDto(profile.Slug, profile.DisplayName, profile.AvatarUrl, profile.Bio, profile.Location,
            profile.Role, KnownBadges(profile.Badges), profile.Background, profile.BackgroundUrl, profile.Effect, profile.AccentColor,
            profile.SongUrl, profile.Links, profile.ViewCount, stats, isOwner);
    }

    // VI: Tạo hồ sơ mặc định cho user mới (gọi trong lúc đăng ký, trước SaveChanges).
    //     Chỉ điền sẵn tên và username, phần còn lại để người dùng tự điền.
    // EN: Builds the default profile for a new user (called during sign-up, before SaveChanges).
    //     Only the name and username are filled in; the user fills in the rest.
    public static async Task<Profile> NewProfileAsync(AppDbContext db, User user, CancellationToken ct) => new()
    {
        User = user,
        Slug = await UniqueSlugAsync(db, user.Username, ct),
        DisplayName = Truncate(string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName, 40),
    };

    // VI: Tài khoản tạo trước khi có tính năng hồ sơ sẽ được tạo hồ sơ khi cần.
    // EN: Accounts created before profiles existed get one on first use.
    internal async Task<Profile> EnsureAsync(Guid userId, CancellationToken ct)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is not null) return profile;

        var user = await db.Users.FindAsync([userId], ct) ?? throw new NotFoundException("User");
        profile = await NewProfileAsync(db, user, ct);
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    internal static async Task<string> UniqueSlugAsync(AppDbContext db, string wanted, CancellationToken ct)
    {
        var baseSlug = Truncate(InvalidSlugChars().Replace(wanted.ToLowerInvariant(), ""), 26);
        if (baseSlug.Length < 3) baseSlug = "user" + baseSlug;
        var slug = baseSlug;
        for (var i = 2; await db.Profiles.AnyAsync(p => p.Slug == slug, ct) || db.Profiles.Local.Any(p => p.Slug == slug); i++)
            slug = $"{baseSlug}{i}";
        return slug;
    }

    internal static bool IsValidSlug(string? slug) => slug is not null && SlugPattern().IsMatch(slug.ToLowerInvariant());

    private async Task<bool> TryCountViewAsync(Profile profile, ProfileVisitor visitor, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var ip = visitor.Ip ?? "unknown";
        // VI: Có id khách (localStorage) thì dùng, không thì dùng IP + trình duyệt.
        // EN: Use the visitor id (from localStorage) when sent, otherwise IP + browser.
        var visitorKey = visitor.VisitorId is { } id && VisitorIdPattern().IsMatch(id)
            ? "v:" + id
            : $"ip:{ip}|{visitor.UserAgent}";
        var visitorHash = Hash($"{profile.UserId}|{day}|{visitorKey}");
        var ipHash = Hash($"{profile.UserId}|{day}|{ip}");

        var views = db.ProfileViews.Where(v => v.ProfileId == profile.UserId && v.Day == day);
        if (await views.AnyAsync(v => v.VisitorHash == visitorHash, ct)) return false;
        if (await views.CountAsync(v => v.IpHash == ipHash, ct) >= MaxViewsPerIpPerDay) return false;

        var view = new ProfileView { ProfileId = profile.UserId, Day = day, VisitorHash = visitorHash, IpHash = ipHash };
        db.ProfileViews.Add(view);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // VI: Cùng khách mở hai tab cùng lúc: chỉ một lượt được tính. EN: Same visitor in two tabs at once: only one counts.
            db.Entry(view).State = EntityState.Detached;
            return false;
        }
    }

    private async Task<ProfileStatsDto> StatsAsync(Guid userId, CancellationToken ct)
    {
        var zone = await db.ResolveTimeZoneAsync(userId, null, ct);
        var today = TimeZones.Today(zone);
        var weekStartUtc = TimeZones.StartOfDayUtc(today.AddDays(-6), zone);

        var work = db.FocusSessions.AsNoTracking().Where(s => s.UserId == userId && s.Kind == SessionKind.Work);
        var totalSeconds = await work.SumAsync(s => (long)s.DurationSeconds, ct);
        var weekSeconds = await work.Where(s => s.StartedAt >= weekStartUtc).SumAsync(s => (long)s.DurationSeconds, ct);
        var lastFocus = await work.MaxAsync(s => (DateTime?)s.EndedAt, ct);
        var tasksDone = await db.Tasks.CountAsync(t => t.UserId == userId && t.IsCompleted, ct);

        var yearAgoUtc = TimeZones.StartOfDayUtc(today.AddDays(-365), zone);
        var activeDays = (await work.Where(s => s.StartedAt >= yearAgoUtc).Select(s => s.StartedAt).ToListAsync(ct))
            .Select(s => TimeZones.LocalDate(s, zone)).ToHashSet();
        var (streak, _) = StatsService.Streaks(activeDays, today);

        return new ProfileStatsDto((int)(totalSeconds / 60), (int)(weekSeconds / 60), tasksDone, streak, lastFocus);
    }

    private string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key + "|" + value)));

    // VI: Bỏ các badge đã bị gỡ (vd. "verified") còn lưu trong hồ sơ cũ.
    // EN: Drop retired badges (e.g. "verified") still stored on old profiles.
    private static List<string> KnownBadges(List<string> badges) => badges.Where(Badges.Contains).ToList();

    private static ProfileDto ToDto(Profile p, ProfileStatsDto stats) =>
        new(p.Slug, p.DisplayName, p.AvatarUrl, p.Bio, p.Location, p.Role, KnownBadges(p.Badges), p.Background, p.BackgroundUrl, p.Effect,
            p.AccentColor, p.SongUrl, p.Links, p.IsPublic, p.IsSuspended, p.ViewCount, stats, p.CreatedAt, p.UpdatedAt);

    private static string? OptionalUrl(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : HttpUrl(value, field);

    // VI: Avatar là link http(s), hoặc ảnh tải lên từ máy dạng data:image (PNG/JPEG/WebP, base64, tối đa ~150 KB).
    //     Kiểm tra cả byte đầu file để chắc đó thật là ảnh.
    // EN: An avatar is an http(s) link, or an image uploaded from the device as data:image (PNG/JPEG/WebP, base64, up to ~150 KB).
    //     The first bytes are checked too, to make sure it really is an image.
    internal static string? AvatarUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return HttpUrl(value, "AvatarUrl");

        var match = AvatarDataUrl().Match(value);
        if (!match.Success || value.Length > Profile.MaxAvatarLength)
            throw new BadRequestException("Avatar image must be a PNG, JPEG or WebP under 150 KB.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(match.Groups["data"].Value); }
        catch (FormatException) { throw new BadRequestException("Avatar image is not valid base64."); }

        var ok = match.Groups["type"].Value switch
        {
            "png" => bytes.AsSpan().StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47]),
            "jpeg" => bytes.AsSpan().StartsWith((byte[])[0xFF, 0xD8, 0xFF]),
            "webp" => bytes.Length > 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!ok) throw new BadRequestException("Avatar image content does not match its type.");
        return value;
    }

    // VI: Chỉ nhận http(s) để không chèn được javascript: hay data:. EN: Only http(s), so no javascript: or data: URLs.
    internal static string HttpUrl(string value, string field)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url) || (url.Scheme != "https" && url.Scheme != "http"))
            throw new BadRequestException($"{field} must be an http(s) URL.");
        if (url.AbsoluteUri.Length > 500)
            throw new BadRequestException($"{field} is too long.");
        return url.AbsoluteUri;
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;

    [GeneratedRegex("^[a-z0-9_.]{3,32}$")] private static partial Regex SlugPattern();
    [GeneratedRegex("^data:image/(?<type>png|jpeg|webp);base64,(?<data>[A-Za-z0-9+/]+={0,2})$")] private static partial Regex AvatarDataUrl();
    [GeneratedRegex("[^a-z0-9_.]")] private static partial Regex InvalidSlugChars();
    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")] private static partial Regex VisitorIdPattern();
}
