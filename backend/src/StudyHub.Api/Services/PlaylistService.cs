using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

public class PlaylistService(AppDbContext db)
{
    public const int MaxTracksPerPlaylist = 500;
    public const int MaxPlaylistsPerUser = 50;
    private const int ShareCodeLength = 10;
    private const string ShareCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public Task<List<PlaylistDto>> ListAsync(Guid userId, CancellationToken ct) =>
        db.Playlists.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.CreatedAt)
            .Select(p => new PlaylistDto(p.Id, p.Name, p.IsDefault, p.Tracks.Count, p.CreatedAt, p.IsShared, p.ShareCode))
            .ToListAsync(ct);

    public async Task<PlaylistDetailDto> GetAsync(Guid userId, Guid id, CancellationToken ct) =>
        ToDetail(await FindAsync(userId, id, includeTracks: true, ct));

    public async Task<PlaylistDetailDto> CreateAsync(Guid userId, SavePlaylistRequest req, CancellationToken ct)
    {
        await EnsureRoomForPlaylistAsync(userId, ct);
        var playlist = new Playlist { UserId = userId, Name = req.Name.Trim() };
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(ct);
        return ToDetail(playlist);
    }

    public async Task<PlaylistDetailDto> RenameAsync(Guid userId, Guid id, SavePlaylistRequest req, CancellationToken ct)
    {
        var playlist = await FindAsync(userId, id, includeTracks: true, ct);
        playlist.Name = req.Name.Trim();
        await db.SaveChangesAsync(ct);
        return ToDetail(playlist);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var playlist = await FindAsync(userId, id, includeTracks: false, ct);
        if (playlist.IsDefault)
            throw new BadRequestException("The Favorites playlist cannot be deleted.");
        db.Playlists.Remove(playlist);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TrackDto> AddTrackAsync(Guid userId, Guid playlistId, AddTrackRequest req, CancellationToken ct)
    {
        var playlist = await FindAsync(userId, playlistId, includeTracks: true, ct);
        var resolved = MusicLinkResolver.Resolve(req.Url);

        if (playlist.Tracks.Count >= MaxTracksPerPlaylist)
            throw new BadRequestException($"A playlist can hold at most {MaxTracksPerPlaylist} tracks.");
        if (playlist.Tracks.Any(t => t.EmbedUrl == resolved.EmbedUrl))
            throw new ConflictException("This track is already in the playlist.");

        var track = new Track
        {
            PlaylistId = playlist.Id,
            Title = string.IsNullOrWhiteSpace(req.Title) ? DefaultTitle(resolved.Provider) : req.Title.Trim(),
            SourceUrl = req.Url.Trim(),
            EmbedUrl = resolved.EmbedUrl,
            Provider = resolved.Provider,
            Position = playlist.Tracks.Count == 0 ? 0 : playlist.Tracks.Max(t => t.Position) + 1
        };
        db.Tracks.Add(track);
        await db.SaveChangesAsync(ct);
        return track.ToDto();
    }

    public async Task RemoveTrackAsync(Guid userId, Guid playlistId, Guid trackId, CancellationToken ct)
    {
        var deleted = await db.Tracks
            .Where(t => t.Id == trackId && t.PlaylistId == playlistId && t.Playlist.UserId == userId)
            .ExecuteDeleteAsync(ct);
        if (deleted == 0)
            throw new NotFoundException("Track");
    }

    public async Task<PlaylistDetailDto> ReorderAsync(Guid userId, Guid playlistId, ReorderTracksRequest req, CancellationToken ct)
    {
        var playlist = await FindAsync(userId, playlistId, includeTracks: true, ct);
        var current = playlist.Tracks.Select(t => t.Id).ToHashSet();
        if (req.TrackIds.Count != current.Count || !current.SetEquals(req.TrackIds))
            throw new BadRequestException("TrackIds must list every track of the playlist exactly once.");

        var byId = playlist.Tracks.ToDictionary(t => t.Id);
        for (var i = 0; i < req.TrackIds.Count; i++)
            byId[req.TrackIds[i]].Position = i;

        await db.SaveChangesAsync(ct);
        return ToDetail(playlist);
    }

    // VI: Bật/tắt chia sẻ. Bật thì tạo mã mới nếu chưa có; tắt thì xóa mã để link cũ hết hiệu lực.
    // EN: Turns sharing on or off. On creates a code when there is none; off clears it so old links stop working.
    public async Task<PlaylistDetailDto> SetSharedAsync(Guid userId, Guid id, SharePlaylistRequest req, CancellationToken ct)
    {
        var playlist = await FindAsync(userId, id, includeTracks: true, ct);
        playlist.IsShared = req.IsShared;
        if (!req.IsShared) playlist.ShareCode = null;
        else playlist.ShareCode ??= await NewShareCodeAsync(ct);
        await db.SaveChangesAsync(ct);
        return ToDetail(playlist);
    }

    // VI: Ai có link cũng xem được. Không thấy nếu chủ playlist bị khóa hoặc hồ sơ bị admin ẩn.
    // EN: Anyone with the link can view it. Hidden when the owner is locked or their profile was suspended by an admin.
    public async Task<SharedPlaylistDto> GetSharedAsync(string code, CancellationToken ct)
    {
        var playlist = await FindSharedAsync(code, ct);
        var profile = playlist.User.Profile;
        var ownerName = profile?.DisplayName is { Length: > 0 } name ? name : playlist.User.DisplayName ?? playlist.User.Username;
        var ownerSlug = profile is { IsPublic: true, IsSuspended: false } ? profile.Slug : null;
        return new SharedPlaylistDto(playlist.ShareCode!, playlist.Name, ownerName, ownerSlug,
            playlist.Tracks.OrderBy(t => t.Position).Select(t => t.ToDto()).ToList());
    }

    // VI: Lưu một bản sao playlist được chia sẻ vào tài khoản của mình (bản sao không được chia sẻ).
    // EN: Saves a copy of a shared playlist into your own account (the copy is not shared).
    public async Task<PlaylistDetailDto> CopySharedAsync(Guid userId, string code, CancellationToken ct)
    {
        var source = await FindSharedAsync(code, ct);
        await EnsureRoomForPlaylistAsync(userId, ct);
        var copy = new Playlist { UserId = userId, Name = source.Name };
        var position = 0;
        foreach (var t in source.Tracks.OrderBy(t => t.Position))
        {
            copy.Tracks.Add(new Track
            {
                Title = t.Title, SourceUrl = t.SourceUrl, EmbedUrl = t.EmbedUrl, Provider = t.Provider, Position = position++
            });
        }
        db.Playlists.Add(copy);
        await db.SaveChangesAsync(ct);
        return ToDetail(copy);
    }

    internal async Task EnsureRoomForPlaylistAsync(Guid userId, CancellationToken ct)
    {
        if (await db.Playlists.CountAsync(p => p.UserId == userId, ct) >= MaxPlaylistsPerUser)
            throw new BadRequestException($"You can have at most {MaxPlaylistsPerUser} playlists.");
    }

    private async Task<Playlist> FindSharedAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code) || code.Length != ShareCodeLength) throw new NotFoundException("Playlist");
        return await db.Playlists.AsNoTracking()
                   .Include(p => p.Tracks)
                   .Include(p => p.User).ThenInclude(u => u.Profile)
                   .FirstOrDefaultAsync(p => p.ShareCode == code && p.IsShared && !p.User.IsDisabled
                                             && (p.User.Profile == null || !p.User.Profile.IsSuspended), ct)
               ?? throw new NotFoundException("Playlist");
    }

    private async Task<string> NewShareCodeAsync(CancellationToken ct)
    {
        while (true)
        {
            var code = RandomNumberGenerator.GetString(ShareCodeChars, ShareCodeLength);
            if (!await db.Playlists.AnyAsync(p => p.ShareCode == code, ct)) return code;
        }
    }

    internal static string DefaultTitle(MusicProvider provider) =>
        provider == MusicProvider.YouTube ? "YouTube video" : "Spotify track";

    private async Task<Playlist> FindAsync(Guid userId, Guid id, bool includeTracks, CancellationToken ct)
    {
        IQueryable<Playlist> query = db.Playlists;
        if (includeTracks) query = query.Include(p => p.Tracks);
        return await query.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct)
               ?? throw new NotFoundException("Playlist");
    }

    private static PlaylistDetailDto ToDetail(Playlist p) =>
        new(p.Id, p.Name, p.IsDefault, p.CreatedAt, p.Tracks.OrderBy(t => t.Position).Select(t => t.ToDto()).ToList(),
            p.IsShared, p.ShareCode);
}
