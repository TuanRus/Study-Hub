namespace StudyHub.Api.Entities;

public class Playlist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    // VI: Playlist "Favorites" tạo sẵn khi đăng ký, thay cho studyhub_music cũ. Không xóa được.
    // EN: The "Favorites" playlist created on sign-up, replacing the old studyhub_music. Cannot be deleted.
    public bool IsDefault { get; set; }

    // VI: Bật chia sẻ thì ai có link /?playlist=<ShareCode> cũng xem và phát được. Tắt chia sẻ thì xóa mã, link cũ hết hiệu lực.
    // EN: When shared, anyone with the /?playlist=<ShareCode> link can view and play it. Unsharing clears the code so old links stop working.
    public bool IsShared { get; set; }
    public string? ShareCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Track> Tracks { get; set; } = [];
}
