namespace StudyHub.Api.Entities;

public enum MusicProvider
{
    YouTube = 0,
    Spotify = 1
}

public class Track
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string EmbedUrl { get; set; } = string.Empty;
    public MusicProvider Provider { get; set; }
    public int Position { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
