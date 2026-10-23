using System.ComponentModel.DataAnnotations;

namespace StudyHub.Api.Dtos;

public record RegisterRequest(
    [Required, StringLength(32, MinimumLength = 3),
     RegularExpression("^[a-zA-Z0-9_.]+$", ErrorMessage = "Username may only contain letters, digits, '_' and '.'.")]
    string Username,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password,
    [StringLength(64)] string? DisplayName = null,
    [StringLength(64)] string? TimeZone = null);

public record LoginRequest(
    [Required] string UsernameOrEmail,
    [Required] string Password);

public record RefreshRequest([Required] string RefreshToken);

public record UserDto(
    Guid Id,
    string Username,
    string Email,
    string? DisplayName,
    string TimeZone,
    DateTime CreatedAt,
    string Role = "User");

public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserDto User);

public record UpdateProfileRequest(
    [StringLength(64)] string? DisplayName,
    [StringLength(64)] string? TimeZone);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(100, MinimumLength = 8)] string NewPassword);
