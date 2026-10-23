using System.ComponentModel.DataAnnotations;

namespace StudyHub.Api.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";

    [Required] public string Issuer { get; set; } = "StudyHub";
    [Required] public string Audience { get; set; } = "StudyHub.Web";

    // VI: Khóa ký HMAC-SHA256, tối thiểu 32 ký tự. Không commit khóa thật, dùng user-secrets hoặc biến môi trường.
    // EN: HMAC-SHA256 signing key, at least 32 characters. Never commit a real key; use user-secrets or env vars.
    [Required, MinLength(32)] public string Key { get; set; } = string.Empty;

    [Range(1, 1440)] public int AccessTokenMinutes { get; set; } = 30;
    [Range(1, 365)] public int RefreshTokenDays { get; set; } = 14;
}
