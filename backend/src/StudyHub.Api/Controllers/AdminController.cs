using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Auth;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
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
    /// <summary>Server overview: users, content, profile views, database size, uptime and health.</summary>
    [HttpGet("stats")]
    public Task<ServerStatsDto> Stats(CancellationToken ct) => admin.StatsAsync(ct);

    /// <summary>Users, newest first. Search matches username, email, display name or profile slug.</summary>
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
}
