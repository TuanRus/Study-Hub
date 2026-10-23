using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

public class PlaylistService(AppDbContext db)
{
    private const int MaxTracksPerPlaylist = 500;

    public Task<List<PlaylistDto>> ListAsync(Guid userId, CancellationToken ct) =>
        db.Playlists.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.CreatedAt)
            .Select(p => new PlaylistDto(p.Id, p.Name, p.IsDefault, p.Tracks.Count, p.CreatedAt))
            .ToListAsync(ct);

    public async Task<PlaylistDetailDto> GetAsync(Guid userId, Guid id, CancellationToken ct) =>
        ToDetail(await FindAsync(userId, id, includeTracks: true, ct));

    public async Task<PlaylistDetailDto> CreateAsync(Guid userId, SavePlaylistRequest req, CancellationToken ct)
    {
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
        new(p.Id, p.Name, p.IsDefault, p.CreatedAt, p.Tracks.OrderBy(t => t.Position).Select(t => t.ToDto()).ToList());
}
