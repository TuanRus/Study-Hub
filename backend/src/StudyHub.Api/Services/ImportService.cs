using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

// VI: Chuyển dữ liệu localStorage cũ lên tài khoản. Gọi lại nhiều lần không tạo bản trùng.
// EN: Moves old localStorage data into the account. Calling it again does not create duplicates.
public class ImportService(AppDbContext db, ProfileService profiles)
{
    private const int MaxItems = 1000;

    public async Task<ImportResultDto> ImportAsync(Guid userId, ImportLocalRequest req, CancellationToken ct)
    {
        if ((req.Todos?.Count ?? 0) > MaxItems || (req.Music?.Count ?? 0) > MaxItems || (req.FocusLog?.Count ?? 0) > MaxItems
            || (req.Playlists?.Count ?? 0) > PlaylistService.MaxPlaylistsPerUser
            || (req.Playlists?.Any(p => (p.Music?.Count ?? 0) > PlaylistService.MaxTracksPerPlaylist) ?? false))
            throw new BadRequestException($"Each list may contain at most {MaxItems} items.");

        var tasks = await ImportTodosAsync(userId, req.Todos ?? [], ct);
        var (tracks, skipped) = await ImportMusicAsync(userId, req.Music ?? [], ct);
        var (playlists, playlistTracks, playlistSkipped) = await ImportPlaylistsAsync(userId, req.Playlists ?? [], ct);
        var sessions = await ImportFocusLogAsync(userId, req.FocusLog ?? [], ct);
        var profileImported = req.Profile is not null && await ImportProfileAsync(userId, req.Profile, ct);

        await db.SaveChangesAsync(ct);
        return new ImportResultDto(tasks, tracks + playlistTracks, skipped + playlistSkipped, sessions, profileImported, playlists);
    }

    private async Task<int> ImportTodosAsync(Guid userId, List<LocalTodo> todos, CancellationToken ct)
    {
        var zone = await db.ResolveTimeZoneAsync(userId, null, ct);
        var existing = (await db.Tasks.Where(t => t.UserId == userId).Select(t => new { t.Title, t.EndAt }).ToListAsync(ct))
            .Select(t => $"{t.Title.ToLowerInvariant()}|{t.EndAt:O}")
            .ToHashSet();
        var count = 0;
        foreach (var todo in todos)
        {
            var title = todo.Text?.Trim();
            if (string.IsNullOrEmpty(title)) continue;
            if (title.Length > 200) title = title[..200];

            var (endAt, isAllDay) = ParseDeadline(todo.Deadline, todo.Time, zone);
            if (!existing.Add($"{title.ToLowerInvariant()}|{endAt:O}")) continue;

            // VI: id cũ là Date.now() nên dùng luôn làm thời điểm tạo.
            // EN: The old id is Date.now(), so reuse it as the creation time.
            var created = todo.Id is > 0 and < 253402300799999
                ? DateTimeOffset.FromUnixTimeMilliseconds(todo.Id.Value).UtcDateTime
                : DateTime.UtcNow;
            db.Tasks.Add(new StudyTask
            {
                UserId = userId,
                Title = title,
                EndAt = endAt,
                IsAllDay = isAllDay,
                Priority = Enum.TryParse<TaskPriority>(todo.Priority, ignoreCase: true, out var p) ? p : TaskPriority.Medium,
                IsCompleted = todo.Completed,
                CompletedAt = todo.Completed ? created : null,
                CreatedAt = created,
                UpdatedAt = created
            });
            count++;
        }
        return count;
    }

    // VI: Chỉ có ngày -> deadline cả ngày; có cả giờ -> deadline lúc đó theo múi giờ của user.
    // EN: Date only -> all-day deadline; date and time -> deadline at that local time.
    private static (DateTime? EndAt, bool IsAllDay) ParseDeadline(string? deadline, string? time, TimeZoneInfo zone)
    {
        if (!DateOnly.TryParseExact(deadline, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return (null, false);
        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            return (date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), true);
        return (TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(at, DateTimeKind.Unspecified), zone), false);
    }

    private async Task<(int Imported, int Skipped)> ImportMusicAsync(Guid userId, List<LocalMusic> music, CancellationToken ct)
    {
        if (music.Count == 0) return (0, 0);

        var favorites = await db.Playlists.Include(p => p.Tracks)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsDefault, ct);
        if (favorites is null)
        {
            favorites = new Playlist { UserId = userId, Name = "Favorites", IsDefault = true };
            db.Playlists.Add(favorites);
        }

        return AddTracks(favorites, music);
    }

    // VI: Playlist cùng tên đã có thì gộp bài vào, không tạo playlist trùng khi nhập lại.
    // EN: A playlist with the same name gets the tracks merged in, so importing again creates no duplicates.
    private async Task<(int Playlists, int Imported, int Skipped)> ImportPlaylistsAsync(
        Guid userId, List<LocalPlaylist> playlists, CancellationToken ct)
    {
        if (playlists.Count == 0) return (0, 0, 0);

        var existing = await db.Playlists.Include(p => p.Tracks).Where(p => p.UserId == userId && !p.IsDefault).ToListAsync(ct);
        var total = await db.Playlists.CountAsync(p => p.UserId == userId, ct);
        int created = 0, imported = 0, skipped = 0;
        foreach (var item in playlists)
        {
            var name = item.Name?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (name.Length > 100) name = name[..100];

            var playlist = existing.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (playlist is null)
            {
                if (total >= PlaylistService.MaxPlaylistsPerUser) break;
                playlist = new Playlist { UserId = userId, Name = name };
                db.Playlists.Add(playlist);
                existing.Add(playlist);
                total++;
                created++;
            }
            var (added, rejected) = AddTracks(playlist, item.Music ?? []);
            imported += added;
            skipped += rejected;
        }
        return (created, imported, skipped);
    }

    private (int Imported, int Skipped) AddTracks(Playlist playlist, List<LocalMusic> music)
    {
        var seen = playlist.Tracks.Select(t => t.EmbedUrl).ToHashSet();
        var position = playlist.Tracks.Count == 0 ? 0 : playlist.Tracks.Max(t => t.Position) + 1;
        int imported = 0, skipped = 0;
        foreach (var item in music)
        {
            ResolvedMusicDto resolved;
            try
            {
                resolved = MusicLinkResolver.Resolve(item.Url ?? string.Empty);
            }
            catch (BadRequestException)
            {
                skipped++;
                continue;
            }
            if (seen.Count >= PlaylistService.MaxTracksPerPlaylist || !seen.Add(resolved.EmbedUrl))
            {
                skipped++;
                continue;
            }

            var title = string.IsNullOrWhiteSpace(item.Name) ? PlaylistService.DefaultTitle(resolved.Provider) : item.Name.Trim();
            db.Tracks.Add(new Track
            {
                Playlist = playlist,
                Title = title.Length > 200 ? title[..200] : title,
                SourceUrl = item.Url!.Trim(),
                EmbedUrl = resolved.EmbedUrl,
                Provider = resolved.Provider,
                Position = position++
            });
            imported++;
        }
        return (imported, skipped);
    }

    private async Task<int> ImportFocusLogAsync(Guid userId, Dictionary<string, LocalFocusDay> log, CancellationToken ct)
    {
        if (log.Count == 0) return 0;

        var zone = await db.ResolveTimeZoneAsync(userId, null, ct);
        var daysWithData = (await db.FocusSessions.Where(s => s.UserId == userId && s.Kind == SessionKind.Work)
                .Select(s => s.StartedAt).ToListAsync(ct))
            .Select(s => TimeZones.LocalDate(s, zone))
            .ToHashSet();

        var count = 0;
        foreach (var (key, day) in log)
        {
            if (!DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;
            if (day.Sessions <= 0 || day.Minutes <= 0 || day.Sessions > 50 || day.Minutes > 1440 || daysWithData.Contains(date))
                continue;

            // VI: Log cũ chỉ có tổng theo ngày, nên chia đều thành các phiên liên tiếp bắt đầu lúc 08:00.
            // EN: The old log only has daily totals, so split them into back-to-back sessions from 08:00.
            var secondsEach = day.Minutes * 60 / day.Sessions;
            var start = TimeZones.StartOfDayUtc(date, zone).AddHours(8);
            for (var i = 0; i < day.Sessions; i++)
            {
                db.FocusSessions.Add(new FocusSession
                {
                    UserId = userId,
                    Kind = SessionKind.Work,
                    StartedAt = start,
                    EndedAt = start.AddSeconds(secondsEach),
                    DurationSeconds = secondsEach
                });
                start = start.AddSeconds(secondsEach);
                count++;
            }
        }
        return count;
    }

    // VI: Dữ liệu cũ có thể sai: trường nào không hợp lệ thì bỏ qua thay vì báo lỗi cả lần nhập.
    // EN: Old data may be invalid: skip bad fields instead of failing the whole import.
    private async Task<bool> ImportProfileAsync(Guid userId, LocalProfile local, CancellationToken ct)
    {
        var profile = await profiles.EnsureAsync(userId, ct);
        if (profile.UpdatedAt is not null) return false;

        string? Url(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try { return ProfileService.HttpUrl(value, "url"); } catch (BadRequestException) { return null; }
        }
        string Clip(string value, int max) => value.Length > max ? value[..max] : value;

        if (!string.IsNullOrWhiteSpace(local.Name)) profile.DisplayName = Clip(local.Name.Trim(), 40);
        var handle = local.Handle?.Trim().TrimStart('@').ToLowerInvariant();
        if (ProfileService.IsValidSlug(handle) && handle != profile.Slug &&
            !await db.Profiles.AnyAsync(p => p.Slug == handle, ct))
            profile.Slug = handle!;
        profile.AvatarUrl = Url(local.Avatar);
        if (local.Bio is not null) profile.Bio = Clip(local.Bio.Trim(), 400);
        profile.Location = string.IsNullOrWhiteSpace(local.Location) ? null : Clip(local.Location.Trim(), 40);
        profile.Role = string.IsNullOrWhiteSpace(local.Role) ? null : Clip(local.Role.Trim(), 40);
        if (local.Badges is not null)
            profile.Badges = local.Badges.Select(b => b.ToLowerInvariant()).Where(ProfileService.Badges.Contains).Distinct().ToList();
        if (local.Background is { } bg && ProfileService.Backgrounds.Contains(bg)) profile.Background = bg;
        profile.BackgroundUrl = Url(local.BackgroundUrl);
        if (local.Effect is { } fx && ProfileService.Effects.Contains(fx)) profile.Effect = fx;
        if (local.Accent is { } accent && System.Text.RegularExpressions.Regex.IsMatch(accent, "^#[0-9a-fA-F]{6}$"))
            profile.AccentColor = accent.ToLowerInvariant();
        var song = Url(local.Song);
        try { if (song is not null) MusicLinkResolver.Resolve(song); } catch (BadRequestException) { song = null; }
        profile.SongUrl = song;
        profile.Links = (local.Links ?? [])
            .Where(l => ProfileService.LinkTypes.Contains(l.Key.ToLowerInvariant()) && Url(l.Value) is not null)
            .ToDictionary(l => l.Key.ToLowerInvariant(), l => Url(l.Value)!);
        profile.UpdatedAt = DateTime.UtcNow;
        return true;
    }
}
