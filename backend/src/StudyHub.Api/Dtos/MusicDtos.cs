using System.ComponentModel.DataAnnotations;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Dtos;

public record ResolveMusicRequest([Required, StringLength(500)] string Url);

public record ResolvedMusicDto(MusicProvider Provider, string EmbedUrl, string ExternalId);

public record TrackDto(
    Guid Id,
    string Title,
    string SourceUrl,
    string EmbedUrl,
    MusicProvider Provider,
    int Position,
    DateTime AddedAt);

public record PlaylistDto(Guid Id, string Name, bool IsDefault, int TrackCount, DateTime CreatedAt);

public record PlaylistDetailDto(Guid Id, string Name, bool IsDefault, DateTime CreatedAt, IReadOnlyList<TrackDto> Tracks);

public record SavePlaylistRequest([Required, StringLength(100, MinimumLength = 1)] string Name);

public record AddTrackRequest(
    [Required, StringLength(500)] string Url,
    [StringLength(200)] string? Title = null);

public record ReorderTracksRequest([Required] List<Guid> TrackIds);
