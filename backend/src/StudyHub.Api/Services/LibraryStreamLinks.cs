using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using StudyHub.Api.Dtos;

namespace StudyHub.Api.Services;

// VI: <audio> không gửi được header Authorization, nên link phát bài NAS mang token ngắn hạn chỉ mở đúng bài đó.
//     Admin nhận link khi nghe kho nhạc; khách nhận link qua playlist được chia sẻ.
// EN: <audio> can't send an Authorization header, so a NAS stream link carries a short-lived token that opens that one song.
//     Admins get links while browsing the library; visitors get them through a shared playlist.
public class LibraryStreamLinks(IDataProtectionProvider protection)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    private readonly ITimeLimitedDataProtector _protector =
        protection.CreateProtector("StudyHub.Library.Stream").ToTimeLimitedDataProtector();

    public LibraryStreamDto Create(string songId)
    {
        var expires = DateTimeOffset.UtcNow.Add(Lifetime);
        var token = _protector.Protect(songId, expires);
        return new LibraryStreamDto($"/api/library/songs/{songId}/stream?token={Uri.EscapeDataString(token)}", expires.UtcDateTime);
    }

    public bool IsValid(string? token, string songId)
    {
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            return _protector.Unprotect(token) == songId;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
