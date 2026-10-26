using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Authorize]
[Tags("Profiles")]
public class ProfilesController(ProfileService profiles, ReportService reports) : ControllerBase
{
    /// <summary>Your own bio-link profile (created on first use).</summary>
    [HttpGet("api/profile")]
    public Task<ProfileDto> GetMine(CancellationToken ct) => profiles.GetMineAsync(User.GetUserId(), ct);

    /// <summary>Replace your profile. The slug is your public link (/@slug) and must be unique.</summary>
    [HttpPut("api/profile")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ProfileDto> Save(SaveProfileRequest req, CancellationToken ct) =>
        profiles.SaveAsync(User.GetUserId(), req, ct);

    /// <summary>Whether a slug is free for you to use.</summary>
    [HttpGet("api/profile/slug-available")]
    public Task<SlugAvailabilityDto> SlugAvailable([FromQuery] string slug, CancellationToken ct) =>
        profiles.CheckSlugAsync(User.GetUserId(), slug, ct);

    /// <summary>
    /// A public profile, no sign-in needed. Each call counts a view, at most once per visitor per day
    /// (send a stable random X-Visitor-Id header; falls back to IP + browser). The owner's own visits never count.
    /// Pass countView=false to read without counting.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("api/profiles/{slug}")]
    [EnableRateLimiting("public")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PublicProfileDto> GetPublic(string slug, [FromQuery] bool countView = true, CancellationToken ct = default)
    {
        Guid? viewer = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        var visitor = new ProfileVisitor(
            Request.Headers["X-Visitor-Id"].FirstOrDefault(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.FirstOrDefault(),
            viewer);
        return profiles.GetPublicAsync(slug, visitor, countView, ct);
    }

    /// <summary>Report someone's profile or account to the admins (spam, abuse...). One open report per account.</summary>
    [HttpPost("api/profiles/{slug}/report")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Report(string slug, CreateReportRequest req, CancellationToken ct)
    {
        await reports.CreateAsync(User.GetUserId(), slug, req, ct);
        return NoContent();
    }
}
