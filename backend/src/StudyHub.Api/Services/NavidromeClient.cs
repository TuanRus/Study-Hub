using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;

namespace StudyHub.Api.Services;

// VI: Kho nhạc NAS qua Navidrome (Lidarr tải nhạc vào thư mục, Navidrome đọc và phát). Đặt qua biến môi trường
//     NAVIDROME_URL (vd. http://navidrome:4533), NAVIDROME_USERNAME, NAVIDROME_PASSWORD. Không bao giờ ghi vào appsettings.
// EN: NAS music library through Navidrome (Lidarr downloads into the folder, Navidrome reads and streams it). Set through the
//     environment variables NAVIDROME_URL (e.g. http://navidrome:4533), NAVIDROME_USERNAME, NAVIDROME_PASSWORD. Never in appsettings.
public class NavidromeOptions
{
    public const string Section = "Navidrome";

    public string? Url { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured => Uri.TryCreate(Url, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password);
}

// VI: Gọi Subsonic API của Navidrome. Mật khẩu chỉ nằm trên server; mỗi request dùng token md5(mật khẩu + salt).
// EN: Calls Navidrome's Subsonic API. The password stays on the server; each request uses the token md5(password + salt).
public partial class NavidromeClient(HttpClient http, IOptions<NavidromeOptions> options, ILogger<NavidromeClient> logger)
{
    private const int MaxResults = 50;
    private readonly NavidromeOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    // VI: Id bài của Navidrome chỉ gồm chữ, số, - và _. EN: Navidrome song ids are letters, digits, - and _ only.
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    public static partial Regex SongIdPattern();

    /// <summary>Songs matching the query, or random songs when the query is empty.</summary>
    public async Task<List<LibrarySongDto>> SearchAsync(string? query, CancellationToken ct)
    {
        EnsureConfigured();
        query = query?.Trim() ?? "";
        var url = query.Length == 0
            ? BuildUrl("getRandomSongs", ("size", MaxResults.ToString()))
            : BuildUrl("search3", ("query", query), ("songCount", MaxResults.ToString()), ("artistCount", "0"), ("albumCount", "0"));

        using var doc = await GetJsonAsync(url, ct);
        var body = doc.RootElement.GetProperty("subsonic-response");
        var container = query.Length == 0 ? "randomSongs" : "searchResult3";
        if (!body.TryGetProperty(container, out var result) || !result.TryGetProperty("song", out var songs))
            return [];

        return songs.EnumerateArray().Select(s => new LibrarySongDto(
            s.GetProperty("id").GetString()!,
            Text(s, "title") ?? "Unknown title",
            Text(s, "artist"),
            Text(s, "album"),
            s.TryGetProperty("duration", out var d) && d.TryGetInt32(out var seconds) ? seconds : null)).ToList();
    }

    /// <summary>"Title · Artist" of a song, or null when Navidrome can't tell (the song still plays).</summary>
    public async Task<string?> TryGetSongTitleAsync(string songId, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        try
        {
            using var doc = await GetJsonAsync(BuildUrl("getSong", ("id", songId)), ct);
            if (!doc.RootElement.GetProperty("subsonic-response").TryGetProperty("song", out var song)) return null;
            var title = Text(song, "title");
            var artist = Text(song, "artist");
            return title is null ? null : artist is null ? title : $"{title} · {artist}";
        }
        catch (AppException)
        {
            return null;
        }
    }

    /// <summary>Starts streaming a song. The caller disposes the response.</summary>
    public async Task<HttpResponseMessage> StreamAsync(string songId, string? range, CancellationToken ct)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl("stream", ("id", songId)));
        if (!string.IsNullOrEmpty(range)) request.Headers.TryAddWithoutValidation("Range", range);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Navidrome is unreachable.");
            throw Unavailable();
        }
        // VI: Subsonic trả lỗi dạng JSON/XML với mã 200, nên kiểm tra cả Content-Type. EN: Subsonic reports errors as JSON/XML with 200, so check the Content-Type too.
        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!response.IsSuccessStatusCode || type.Contains("json") || type.Contains("xml"))
        {
            response.Dispose();
            throw new NotFoundException("Song");
        }
        return response;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        JsonDocument doc;
        try
        {
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            logger.LogWarning(ex, "Navidrome request failed.");
            throw Unavailable();
        }

        var body = doc.RootElement.TryGetProperty("subsonic-response", out var b) ? b : default;
        if (body.ValueKind != JsonValueKind.Object || Text(body, "status") != "ok")
        {
            var error = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var e) ? Text(e, "message") : null;
            logger.LogWarning("Navidrome returned an error: {Error}", error ?? "unknown");
            doc.Dispose();
            throw Unavailable();
        }
        return doc;
    }

    private string BuildUrl(string method, params (string Key, string Value)[] args)
    {
        var salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var token = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(_options.Password + salt)));
        var query = new[] { ("u", _options.Username!), ("t", token), ("s", salt), ("v", "1.16.1"), ("c", "StudyHub"), ("f", "json") }
            .Concat(args)
            .Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}");
        return $"{_options.Url!.TrimEnd('/')}/rest/{method}?{string.Join('&', query)}";
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new AppException(StatusCodes.Status503ServiceUnavailable, "Music library is off",
                "The NAS music library is not set up on this server.");
    }

    private static AppException Unavailable() =>
        new(StatusCodes.Status502BadGateway, "Music library unavailable", "Could not reach the NAS music library.");

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
