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
[Tags("Music")]
public class MusicController(PlaylistService playlists, LibraryStreamLinks nasLinks) : ControllerBase
{
    /// <summary>Turn a YouTube or Spotify link into an embeddable URL. Works without signing in.</summary>
    [AllowAnonymous]
    [HttpPost("api/music/resolve")]
    public ResolvedMusicDto Resolve(ResolveMusicRequest req) => MusicLinkResolver.Resolve(req.Url);

    [HttpGet("api/playlists")]
    public Task<List<PlaylistDto>> List(CancellationToken ct) => playlists.ListAsync(User.GetUserId(), ct);

    [HttpGet("api/playlists/{id:guid}")]
    public Task<PlaylistDetailDto> Get(Guid id, CancellationToken ct) => playlists.GetAsync(User.GetUserId(), id, ct);

    [HttpPost("api/playlists")]
    [ProducesResponseType<PlaylistDetailDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SavePlaylistRequest req, CancellationToken ct)
    {
        var playlist = await playlists.CreateAsync(User.GetUserId(), req, ct);
        return CreatedAtAction(nameof(Get), new { id = playlist.Id }, playlist);
    }

    [HttpPut("api/playlists/{id:guid}")]
    public Task<PlaylistDetailDto> Rename(Guid id, SavePlaylistRequest req, CancellationToken ct) =>
        playlists.RenameAsync(User.GetUserId(), id, req, ct);

    [HttpDelete("api/playlists/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await playlists.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    /// <summary>Add a YouTube/Spotify link to a playlist. Admins can also add NAS songs ("nas:&lt;id&gt;").</summary>
    [HttpPost("api/playlists/{id:guid}/tracks")]
    [ProducesResponseType<TrackDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddTrack(Guid id, AddTrackRequest req, CancellationToken ct)
    {
        var track = await playlists.AddTrackAsync(User.GetUserId(), id, req,
            User.IsInRole(nameof(UserRole.Admin)) || User.IsInRole(nameof(UserRole.SuperAdmin)), ct);
        return StatusCode(StatusCodes.Status201Created, track);
    }

    [HttpDelete("api/playlists/{id:guid}/tracks/{trackId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveTrack(Guid id, Guid trackId, CancellationToken ct)
    {
        await playlists.RemoveTrackAsync(User.GetUserId(), id, trackId, ct);
        return NoContent();
    }

    /// <summary>Set the track order. Send every track id in the new order.</summary>
    [HttpPut("api/playlists/{id:guid}/tracks/order")]
    public Task<PlaylistDetailDto> Reorder(Guid id, ReorderTracksRequest req, CancellationToken ct) =>
        playlists.ReorderAsync(User.GetUserId(), id, req, ct);

    /// <summary>Share a playlist by link (or stop sharing it). Stopping clears the code, so old links stop working.</summary>
    [HttpPut("api/playlists/{id:guid}/share")]
    public Task<PlaylistDetailDto> Share(Guid id, SharePlaylistRequest req, CancellationToken ct) =>
        playlists.SetSharedAsync(User.GetUserId(), id, req, ct);

    /// <summary>A shared playlist, no sign-in needed. The code comes from the share link (/?playlist=code).</summary>
    [AllowAnonymous]
    [HttpGet("api/shared-playlists/{code}")]
    [EnableRateLimiting("public")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SharedPlaylistDto> GetShared(string code, CancellationToken ct) => playlists.GetSharedAsync(code, ct);

    /// <summary>A short-lived link to listen to a NAS song of a shared playlist, no sign-in needed.</summary>
    [AllowAnonymous]
    [HttpGet("api/shared-playlists/{code}/tracks/{trackId:guid}/stream-url")]
    [EnableRateLimiting("public")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<LibraryStreamDto> SharedStreamUrl(string code, Guid trackId, CancellationToken ct) =>
        nasLinks.Create(await playlists.GetSharedNasSongIdAsync(code, trackId, ct));

    /// <summary>Save a copy of a shared playlist to your own playlists. NAS songs are not copied.</summary>
    [HttpPost("api/shared-playlists/{code}/copy")]
    [ProducesResponseType<PlaylistDetailDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CopyShared(string code, CancellationToken ct)
    {
        var playlist = await playlists.CopySharedAsync(User.GetUserId(), code, ct);
        return CreatedAtAction(nameof(Get), new { id = playlist.Id }, playlist);
    }
}
