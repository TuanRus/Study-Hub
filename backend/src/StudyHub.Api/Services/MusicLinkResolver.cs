using System.Text.RegularExpressions;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

// VI: Đổi link YouTube/Spotify người dùng dán vào thành URL nhúng iframe.
// EN: Turns a pasted YouTube/Spotify link into an iframe embed URL.
public static partial class MusicLinkResolver
{
    private static readonly HashSet<string> SpotifyTypes = ["track", "album", "playlist", "episode", "show", "artist"];

    // VI: allowNas = cho phép bài NAS "nas:<id>" (chỉ khi admin thêm vào playlist). Hồ sơ, import... không bao giờ nhận bài NAS.
    // EN: allowNas = accept a NAS song "nas:<id>" (only when an admin adds it to a playlist). Profiles, imports... never take NAS songs.
    public static ResolvedMusicDto Resolve(string input, bool allowNas = false)
    {
        var raw = input.Trim();

        // VI: Bài NAS: "nas:<id bài Navidrome>". EN: NAS song: "nas:<Navidrome song id>".
        if (allowNas && raw.StartsWith("nas:", StringComparison.Ordinal))
        {
            var songId = raw[4..];
            if (!NavidromeClient.SongIdPattern().IsMatch(songId))
                throw new BadRequestException("Not a valid NAS song.");
            return new(MusicProvider.Nas, $"nas:{songId}", songId);
        }

        var uri = SpotifyUri().Match(raw);
        if (uri.Success)
            return Spotify(uri.Groups[1].Value, uri.Groups[2].Value);

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || (url.Scheme != "https" && url.Scheme != "http"))
            throw new BadRequestException("Not a valid URL.");

        var host = url.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];

        if (host is "youtu.be")
            return YouTubeVideo(url.AbsolutePath.Trim('/'));

        if (host is "youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtube-nocookie.com")
        {
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            var segments = url.AbsolutePath.Trim('/').Split('/');
            if (segments[0] is "embed" or "shorts" or "live" && segments.Length > 1)
                return YouTubeVideo(segments[1]);
            if (query["v"] is { } v)
                return YouTubeVideo(v);
            if (query["list"] is { } list && PlaylistId().IsMatch(list))
                return new(MusicProvider.YouTube, $"https://www.youtube.com/embed/videoseries?list={list}", list);
        }

        if (host is "open.spotify.com" or "play.spotify.com")
        {
            // VI: Bỏ tiền tố ngôn ngữ như /intl-vi/.
            // EN: Skip a locale prefix such as /intl-vi/.
            var segments = url.AbsolutePath.Trim('/').Split('/').SkipWhile(s => s.StartsWith("intl-") || s == "embed").ToArray();
            if (segments.Length >= 2)
                return Spotify(segments[0], segments[1]);
        }

        throw new BadRequestException("Only YouTube and Spotify links are supported.");
    }

    private static ResolvedMusicDto YouTubeVideo(string id)
    {
        if (!VideoId().IsMatch(id))
            throw new BadRequestException("Could not find a YouTube video id in this link.");
        return new(MusicProvider.YouTube, $"https://www.youtube.com/embed/{id}", id);
    }

    private static ResolvedMusicDto Spotify(string type, string id)
    {
        if (!SpotifyTypes.Contains(type) || !SpotifyId().IsMatch(id))
            throw new BadRequestException("Could not find a Spotify item in this link.");
        return new(MusicProvider.Spotify, $"https://open.spotify.com/embed/{type}/{id}", $"{type}:{id}");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")] private static partial Regex VideoId();
    [GeneratedRegex("^[A-Za-z0-9_-]{10,64}$")] private static partial Regex PlaylistId();
    [GeneratedRegex("^[A-Za-z0-9]{22}$")] private static partial Regex SpotifyId();
    [GeneratedRegex("^spotify:([a-z]+):([A-Za-z0-9]{22})$")] private static partial Regex SpotifyUri();
}
