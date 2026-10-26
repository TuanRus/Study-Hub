namespace StudyHub.Api.Dtos;

// VI: Kho nhạc NAS (Navidrome). Enabled = false khi server chưa cấu hình NAVIDROME_*.
// EN: NAS music library (Navidrome). Enabled = false when the server has no NAVIDROME_* settings.
public record LibraryStatusDto(bool Enabled);

public record LibrarySongDto(string Id, string Title, string? Artist, string? Album, int? DurationSeconds);

// VI: Link phát có token ngắn hạn (xem LibraryStreamLinks). EN: A stream link with a short-lived token (see LibraryStreamLinks).
public record LibraryStreamDto(string Url, DateTime ExpiresAt);
