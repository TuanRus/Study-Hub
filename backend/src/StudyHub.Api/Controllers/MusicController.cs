using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Authorize]
[Tags("Music")]
public class MusicController(PlaylistService playlists) : ControllerBase
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

    /// <summary>Add a YouTube/Spotify link to a playlist.</summary>
    [HttpPost("api/playlists/{id:guid}/tracks")]
    [ProducesResponseType<TrackDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddTrack(Guid id, AddTrackRequest req, CancellationToken ct)
    {
        var track = await playlists.AddTrackAsync(User.GetUserId(), id, req, ct);
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
}
