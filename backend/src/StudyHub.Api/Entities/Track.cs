namespace StudyHub.Api.Entities;

public enum MusicProvider
{
    YouTube = 0,
    Spotify = 1,
    // VI: Bài trong kho nhạc NAS (Navidrome), SourceUrl = "nas:<id>". Chỉ admin thêm được. EN: A song from the NAS library (Navidrome), SourceUrl = "nas:<id>". Admins only.
    Nas = 2
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
