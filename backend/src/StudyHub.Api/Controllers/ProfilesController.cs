using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Authorize]
[Tags("Profiles")]
public class ProfilesController(ProfileService profiles, ReportService reports, NavidromeClient navidrome, LibraryStreamLinks nasLinks) : ControllerBase
{
    /// <summary>Your own bio-link profile (created on first use).</summary>
    [HttpGet("api/profile")]
    public Task<ProfileDto> GetMine(CancellationToken ct) => profiles.GetMineAsync(User.GetUserId(), ct);

    /// <summary>Replace your profile. The slug is your public link (/@slug) and must be unique.</summary>
    [HttpPut("api/profile")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ProfileDto> Save(SaveProfileRequest req, CancellationToken ct) =>
        profiles.SaveAsync(User.GetUserId(), req,
            User.IsInRole(nameof(UserRole.Admin)) || User.IsInRole(nameof(UserRole.SuperAdmin)), ct);

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

    /// <summary>A short-lived link to the profile song when it is a NAS song, no sign-in needed.</summary>
    [AllowAnonymous]
    [HttpGet("api/profiles/{slug}/song/stream-url")]
    [EnableRateLimiting("public")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<LibraryStreamDto> SongStreamUrl(string slug, CancellationToken ct)
    {
        var songId = await profiles.GetPublicNasSongIdAsync(slug, ct);
        return nasLinks.Create(songId) with { Title = await navidrome.TryGetSongTitleAsync(songId, ct) };
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
