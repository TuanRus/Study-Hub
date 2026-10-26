using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Auth;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

// VI: Kho nhạc riêng trên NAS: chỉ admin duyệt được. Khách chỉ nghe được bài NAS nằm trong playlist admin đã chia sẻ.
// EN: The private NAS music library: only admins browse it. Visitors only hear NAS songs inside a playlist an admin shared.
[ApiController]
[Authorize(Policy = Policies.Admin)]
[Tags("Library")]
public class LibraryController(NavidromeClient navidrome, LibraryStreamLinks links) : ControllerBase
{
    [HttpGet("api/library/status")]
    public LibraryStatusDto Status() => new(navidrome.IsConfigured);

    /// <summary>Search the NAS library. An empty query returns random songs.</summary>
    [HttpGet("api/library/songs")]
    public Task<List<LibrarySongDto>> Search([FromQuery] string? q, CancellationToken ct)
    {
        if (q?.Length > 200) throw new BadRequestException("The search is too long.");
        return navidrome.SearchAsync(q, ct);
    }

    /// <summary>A short-lived link an &lt;audio&gt; element can play.</summary>
    [HttpGet("api/library/songs/{id}/stream-url")]
    public LibraryStreamDto StreamUrl(string id)
    {
        if (!NavidromeClient.SongIdPattern().IsMatch(id)) throw new NotFoundException("Song");
        return links.Create(id);
    }

    /// <summary>Streams a song from Navidrome. Needs the token from a stream-url; seeking (Range) is passed through.</summary>
    [AllowAnonymous]
    [HttpGet("api/library/songs/{id}/stream")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task Stream(string id, [FromQuery] string? token, CancellationToken ct)
    {
        if (!NavidromeClient.SongIdPattern().IsMatch(id) || !links.IsValid(token, id))
            throw new UnauthorizedException("This stream link is invalid or has expired.");

        using var upstream = await navidrome.StreamAsync(id, Request.Headers.Range.ToString(), ct);
        Response.StatusCode = (int)upstream.StatusCode;
        Response.Headers.CacheControl = "private, no-store";
        var content = upstream.Content.Headers;
        Response.ContentType = content.ContentType?.ToString() ?? "audio/mpeg";
        if (content.ContentLength is { } length) Response.ContentLength = length;
        if (content.ContentRange is { } range) Response.Headers.ContentRange = range.ToString();
        Response.Headers.AcceptRanges = "bytes";
        await using var body = await upstream.Content.ReadAsStreamAsync(ct);
        await body.CopyToAsync(Response.Body, ct);
    }
}
