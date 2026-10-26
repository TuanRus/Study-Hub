using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Auth;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

// VI: Admin và SuperAdmin dùng được; riêng đổi quyền chỉ SuperAdmin. Admin chỉ tác động được lên user thường.
// EN: For Admins and SuperAdmins; changing roles is SuperAdmin only. Admins can only act on regular users.
[ApiController]
[Authorize(Policy = Policies.Admin)]
[Route("api/admin")]
[Tags("Admin")]
public class AdminController(AdminService admin) : ControllerBase
{
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress is { } ip ? IpBanList.Normalize(ip) : null;

    /// <summary>Server overview: users, content, profile views, database size, uptime and health.</summary>
    [HttpGet("stats")]
    public Task<ServerStatsDto> Stats(CancellationToken ct) => admin.StatsAsync(ct);

    /// <summary>Users, newest first. Search matches username, email, display name, profile slug or IP.
    /// Filter by role and by status (flagged, suspicious = many tasks in 24 hours, disabled).</summary>
    [HttpGet("users")]
    public Task<AdminUserPageDto> Users([FromQuery] AdminUserQuery query, CancellationToken ct) =>
        admin.ListUsersAsync(query, ct);

    /// <summary>Lock or unlock an account. Locking signs the user out everywhere.</summary>
    [HttpPut("users/{id:guid}/status")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<AdminUserDto> SetStatus(Guid id, SetUserStatusRequest req, CancellationToken ct) =>
        admin.SetStatusAsync(User.GetUserId(), id, req, ct);

    /// <summary>Make a user an Admin or turn an Admin back into a User. Super admins only.</summary>
    [Authorize(Policy = Policies.SuperAdmin)]
    [HttpPut("users/{id:guid}/role")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<AdminUserDto> SetRole(Guid id, SetUserRoleRequest req, CancellationToken ct) =>
        admin.SetRoleAsync(User.GetUserId(), id, req, ct);

    /// <summary>Delete a user and all their data.</summary>
    [HttpDelete("users/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken ct)
    {
        await admin.DeleteUserAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    /// <summary>Hide (or restore) a user's public profile.</summary>
    [HttpPut("profiles/{userId:guid}/moderation")]
    public Task<AdminUserDto> ModerateProfile(Guid userId, ModerateProfileRequest req, CancellationToken ct) =>
        admin.ModerateProfileAsync(User.GetUserId(), userId, req, ct);

    /// <summary>Star (flag) or unstar an account as abusive, with an optional reason. Only a marker.</summary>
    [HttpPut("users/{id:guid}/flag")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<AdminUserDto> Flag(Guid id, FlagUserRequest req, CancellationToken ct) =>
        admin.FlagAsync(User.GetUserId(), id, req, ct);

    /// <summary>Delete a user's junk data (tasks, focus sessions, tracks, profile text), optionally only the last N hours.</summary>
    [HttpPost("users/{id:guid}/cleanup")]
    public Task<CleanupResultDto> Cleanup(Guid id, CleanupUserRequest req, CancellationToken ct) =>
        admin.CleanupAsync(User.GetUserId(), id, req, ct);

    /// <summary>Ban the IPs a user signed up and last signed in from, and lock the account (unless disableAccount is false).</summary>
    [HttpPost("users/{id:guid}/ban-ip")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<List<IpBanDto>> BanUserIp(Guid id, BanUserIpRequest req, CancellationToken ct) =>
        admin.BanUserIpAsync(User.GetUserId(), ClientIp, id, req, ct);

    /// <summary>Banned IPs, newest first. Every /api request from them gets 403.</summary>
    [HttpGet("bans")]
    public Task<List<IpBanDto>> Bans(CancellationToken ct) => admin.ListBansAsync(ct);

    /// <summary>Ban an IP address by hand. Private/LAN addresses are refused.</summary>
    [HttpPost("bans")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IpBanDto> BanIp(BanIpRequest req, CancellationToken ct) => admin.BanIpAsync(User.GetUserId(), ClientIp, req, ct);

    /// <summary>Lift an IP ban.</summary>
    [HttpDelete("bans/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unban(Guid id, CancellationToken ct)
    {
        await admin.UnbanIpAsync(id, ct);
        return NoContent();
    }

    /// <summary>Anti-spam limits in use: tasks per day that star an account, daily task cap, reports that star an account.</summary>
    [HttpGet("settings/abuse")]
    public AbuseSettingsDto AbuseSettings() => admin.GetAbuseSettings();

    /// <summary>Change the anti-spam limits (0 turns one off). Super admins only.</summary>
    [Authorize(Policy = Policies.SuperAdmin)]
    [HttpPut("settings/abuse")]
    public Task<AbuseSettingsDto> SaveAbuseSettings(SaveAbuseSettingsRequest req, CancellationToken ct) =>
        admin.SaveAbuseSettingsAsync(User.GetUserId(), req, ct);

    /// <summary>Reports sent by users, newest first (up to 100). Filter by status.</summary>
    [HttpGet("reports")]
    public Task<AdminReportPageDto> Reports([FromQuery] ReportStatus? status, CancellationToken ct) =>
        admin.ListReportsAsync(status, ct);

    /// <summary>Mark a report resolved, dismissed or open again.</summary>
    [HttpPut("reports/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> HandleReport(Guid id, HandleReportRequest req, CancellationToken ct)
    {
        await admin.HandleReportAsync(User.GetUserId(), id, req, ct);
        return NoContent();
    }
}
