using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("Auth")]
public class AuthController(AuthService auth, PasswordResetService passwordReset) : ControllerBase
{
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress is { } ip ? IpBanList.Normalize(ip) : null;

    /// <summary>Create an account and sign in.</summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(req, ClientIp, ct);
        return CreatedAtAction(nameof(Me), result);
    }

    /// <summary>Sign in with username or email.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AuthResponse> Login(LoginRequest req, CancellationToken ct) => auth.LoginAsync(req, ClientIp, ct);

    /// <summary>Swap a refresh token for a new token pair. The old refresh token stops working.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AuthResponse> Refresh(RefreshRequest req, CancellationToken ct) => auth.RefreshAsync(req.RefreshToken, ClientIp, ct);

    /// <summary>Revoke a refresh token.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest req, CancellationToken ct)
    {
        await auth.LogoutAsync(req.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Email a 6-digit code to reset the password. Same answer whether or not the email is registered.</summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req, CancellationToken ct)
    {
        await passwordReset.RequestAsync(req, ct);
        return NoContent();
    }

    /// <summary>Set a new password with the emailed code. Signs out every device.</summary>
    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req, CancellationToken ct)
    {
        await passwordReset.ResetAsync(req, ct);
        return NoContent();
    }

    /// <summary>The signed-in user's profile.</summary>
    [Authorize]
    [HttpGet("me")]
    public Task<UserDto> Me(CancellationToken ct) => auth.GetMeAsync(User.GetUserId(), ct);

    /// <summary>Change display name or time zone (IANA id, e.g. Asia/Ho_Chi_Minh).</summary>
    [Authorize]
    [HttpPut("me")]
    public Task<UserDto> UpdateMe(UpdateProfileRequest req, CancellationToken ct) =>
        auth.UpdateProfileAsync(User.GetUserId(), req, ct);

    /// <summary>Change password. Signs out every other device.</summary>
    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(User.GetUserId(), req, ct);
        return NoContent();
    }
}
