using System.ComponentModel.DataAnnotations;

namespace StudyHub.Api.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";

    [Required] public string Issuer { get; set; } = "StudyHub";
    [Required] public string Audience { get; set; } = "StudyHub.Web";

    public const string MissingKeyMessage =
        "Jwt:Key is missing or shorter than 32 characters. " +
        "Development: run `dotnet user-secrets set \"Jwt:Key\" \"<random 32+ chars>\"` in src/StudyHub.Api. " +
        "Docker/server: set JWT_KEY in .env (or the Jwt__Key environment variable).";

    // VI: Khóa ký HMAC-SHA256, tối thiểu 32 ký tự. Không có khóa nào trong appsettings: dùng user-secrets hoặc biến môi trường.
    //     Thiếu khóa thì server dừng ngay lúc khởi động với thông báo trên.
    // EN: HMAC-SHA256 signing key, at least 32 characters. No key lives in appsettings; use user-secrets or env vars.
    //     Without a key the server stops at start-up with the message above.
    [Required(ErrorMessage = MissingKeyMessage), MinLength(32, ErrorMessage = MissingKeyMessage)]
    public string Key { get; set; } = string.Empty;

    [Range(1, 1440)] public int AccessTokenMinutes { get; set; } = 30;
    [Range(1, 365)] public int RefreshTokenDays { get; set; } = 14;
}
