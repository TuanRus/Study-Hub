using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StudyHub.Api.Dtos;
using StudyHub.Api.Services;

namespace StudyHub.Api.Tests;

// VI: Kho nhạc NAS qua Navidrome, với một Navidrome giả trả lời Subsonic API.
// EN: The NAS music library through Navidrome, against a fake Navidrome answering the Subsonic API.
public class LibraryTests
{
    private const string Password = "nas-test-password";

    private sealed class FakeNavidrome : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/rest/search3"))
                return Task.FromResult(Json("""{"subsonic-response":{"status":"ok","searchResult3":{"song":[{"id":"abc123","title":"Lofi Song","artist":"Chill","album":"Beats","duration":185}]}}}"""));
            if (path.EndsWith("/rest/getSong"))
                return Task.FromResult(Json("""{"subsonic-response":{"status":"ok","song":{"id":"abc123","title":"Lofi Song","artist":"Chill"}}}"""));
            if (path.EndsWith("/rest/getRandomSongs"))
                return Task.FromResult(Json("""{"subsonic-response":{"status":"ok","randomSongs":{"song":[{"id":"r1","title":"Random"}]}}}"""));
            if (path.EndsWith("/rest/getArtists"))
                return Task.FromResult(Json("""{"subsonic-response":{"status":"ok","artists":{"index":[{"name":"C","artist":[{"id":"ar1","name":"Chill","albumCount":2}]},{"name":"A","artist":[{"id":"ar2","name":"Ambient","albumCount":1}]}]}}}"""));
            if (path.EndsWith("/rest/getArtist"))
                return Task.FromResult(request.RequestUri.Query.Contains("id=ar1")
                    ? Json("""{"subsonic-response":{"status":"ok","artist":{"id":"ar1","name":"Chill","album":[{"id":"al1"},{"id":"al2"}]}}}""")
                    : Json("""{"subsonic-response":{"status":"failed","error":{"code":70,"message":"Artist not found"}}}"""));
            if (path.EndsWith("/rest/getAlbum"))
                return Task.FromResult(request.RequestUri.Query.Contains("id=al1")
                    ? Json("""{"subsonic-response":{"status":"ok","album":{"id":"al1","song":[{"id":"s1","title":"One","artist":"Chill","album":"Beats"},{"id":"s2","title":"Two","artist":"Chill","album":"Beats"}]}}}""")
                    : Json("""{"subsonic-response":{"status":"ok","album":{"id":"al2","song":[{"id":"s3","title":"Three","artist":"Chill","album":"Waves"}]}}}"""));
            if (path.EndsWith("/rest/stream"))
            {
                if (request.RequestUri.Query.Contains("id=missing"))
                    return Task.FromResult(Json("""{"subsonic-response":{"status":"failed","error":{"code":70,"message":"not found"}}}"""));
                var partial = request.Headers.Range is not null;
                var res = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.ASCII.GetBytes(partial ? "ID3" : "ID3-full-song")),
                };
                res.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
                if (partial) res.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2, 13);
                return Task.FromResult(res);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class LibraryFactory(bool configured, bool viaEnvNames = false) : StudyHubFactory
    {
        public FakeNavidrome Navidrome { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // VI: Luôn đặt rõ, để Navidrome thật trong user-secrets của máy dev không lọt vào test.
            // EN: Always set explicitly, so a real Navidrome in the dev machine's user-secrets never leaks into tests.
            var direct = configured && !viaEnvNames;
            builder.UseSetting("Navidrome:Url", direct ? "http://navidrome.test:4533/" : "");
            builder.UseSetting("Navidrome:Username", direct ? "studyhub" : "");
            builder.UseSetting("Navidrome:Password", direct ? Password : "");
            // VI: Tên biến trong .env (docker run --env-file). EN: The .env names (docker run --env-file).
            if (configured && viaEnvNames)
            {
                builder.UseSetting("NAVIDROME_URL", "http://navidrome.test:4533/");
                builder.UseSetting("NAVIDROME_USERNAME", "studyhub");
                builder.UseSetting("NAVIDROME_PASSWORD", Password);
            }
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<NavidromeClient>().ConfigurePrimaryHttpMessageHandler(() => Navidrome));
        }
    }

    [Fact]
    public async Task Regular_users_and_visitors_cannot_use_the_library()
    {
        using var factory = new LibraryFactory(configured: true);
        var user = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/library/songs?q=lofi")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/library/songs/abc123/stream-url")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/library/status")).StatusCode);
        Assert.Empty(factory.Navidrome.Requests);
    }

    [Fact]
    public async Task Without_settings_the_library_is_off()
    {
        using var factory = new LibraryFactory(configured: false);
        var admin = await factory.CreateSuperAdminClientAsync();
        var status = await admin.GetFromJsonAsync<LibraryStatusDto>("/api/library/status", TestJson.Options);
        Assert.False(status!.Enabled);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.GetAsync("/api/library/songs?q=x")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_searches_with_a_token_and_never_sends_the_password(bool viaEnvNames)
    {
        using var factory = new LibraryFactory(configured: true, viaEnvNames);
        var admin = await factory.CreateSuperAdminClientAsync();
        Assert.True((await admin.GetFromJsonAsync<LibraryStatusDto>("/api/library/status", TestJson.Options))!.Enabled);

        var songs = await admin.GetFromJsonAsync<List<LibrarySongDto>>("/api/library/songs?q=lofi", TestJson.Options);
        var song = Assert.Single(songs!);
        Assert.Equal(new LibrarySongDto("abc123", "Lofi Song", "Chill", "Beats", 185), song);

        var random = await admin.GetFromJsonAsync<List<LibrarySongDto>>("/api/library/songs", TestJson.Options);
        Assert.Equal("r1", Assert.Single(random!).Id);

        foreach (var request in factory.Navidrome.Requests)
        {
            var query = request.RequestUri!.Query;
            Assert.Contains("u=studyhub", query);
            Assert.Contains("t=", query);
            Assert.Contains("s=", query);
            Assert.DoesNotContain(Password, query);
            Assert.DoesNotContain("p=", query);
        }
    }

    [Fact]
    public async Task Admin_browses_by_artist()
    {
        using var factory = new LibraryFactory(configured: true);
        var user = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/library/artists")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/library/artists/ar1/songs")).StatusCode);
        Assert.Empty(factory.Navidrome.Requests);

        var admin = await factory.CreateSuperAdminClientAsync();
        var artists = await admin.GetFromJsonAsync<List<LibraryArtistDto>>("/api/library/artists", TestJson.Options);
        Assert.Equal([new LibraryArtistDto("ar2", "Ambient", 1), new LibraryArtistDto("ar1", "Chill", 2)], artists);

        var songs = (await admin.GetFromJsonAsync<List<LibrarySongDto>>("/api/library/artists/ar1/songs", TestJson.Options))!;
        Assert.Equal(["s1", "s2", "s3"], songs.Select(s => s.Id));
        Assert.Equal("Waves", songs[2].Album);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/library/artists/nobody/songs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/library/artists/bad%20id/songs")).StatusCode);
    }

    [Fact]
    public async Task Stream_link_plays_that_song_only_and_passes_range_through()
    {
        using var factory = new LibraryFactory(configured: true);
        var admin = await factory.CreateSuperAdminClientAsync();
        var link = await admin.GetFromJsonAsync<LibraryStreamDto>("/api/library/songs/abc123/stream-url", TestJson.Options);
        Assert.True(link!.ExpiresAt > DateTime.UtcNow);

        // VI: <audio> không gửi Authorization, nên dùng client không đăng nhập. EN: <audio> sends no Authorization, so use a signed-out client.
        var visitor = factory.CreateClient();
        var full = await visitor.GetAsync(link.Url);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal("audio/mpeg", full.Content.Headers.ContentType!.MediaType);
        Assert.Equal("ID3-full-song", await full.Content.ReadAsStringAsync());

        var request = new HttpRequestMessage(HttpMethod.Get, link.Url);
        request.Headers.Range = new RangeHeaderValue(0, 2);
        var partial = await visitor.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("bytes 0-2/13", partial.Content.Headers.ContentRange!.ToString());

        // VI: Token của bài này không mở được bài khác. EN: This song's token does not open another song.
        var other = link.Url.Replace("/songs/abc123/", "/songs/other1/");
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(other)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/library/songs/abc123/stream")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/library/songs/abc123/stream?token=forged")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_add_nas_songs_and_nowhere_else_takes_them()
    {
        using var factory = new LibraryFactory(configured: true);
        var user = await factory.CreateUserClientAsync();
        var playlist = await (await user.PostAsJsonAsync("/api/playlists", new SavePlaylistRequest("Mine")))
            .Content.ReadFromJsonAsync<PlaylistDetailDto>(TestJson.Options);
        var res = await user.PostAsJsonAsync($"/api/playlists/{playlist!.Id}/tracks", new AddTrackRequest("nas:abc123", "Sneaky"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Throws<StudyHub.Api.Common.BadRequestException>(() => MusicLinkResolver.Resolve("nas:abc123"));

        var admin = await factory.CreateSuperAdminClientAsync();
        var adminList = await (await admin.PostAsJsonAsync("/api/playlists", new SavePlaylistRequest("NAS mix")))
            .Content.ReadFromJsonAsync<PlaylistDetailDto>(TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/playlists/{adminList!.Id}/tracks", new AddTrackRequest("nas:bad id"))).StatusCode);
        var added = await admin.PostAsJsonAsync($"/api/playlists/{adminList.Id}/tracks", new AddTrackRequest("nas:abc123", "Lofi Song"));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var track = await added.Content.ReadFromJsonAsync<TrackDto>(TestJson.Options);
        Assert.Equal(StudyHub.Api.Entities.MusicProvider.Nas, track!.Provider);
        Assert.Equal("nas:abc123", track.SourceUrl);
    }

    [Fact]
    public async Task Shared_playlist_lets_visitors_hear_its_nas_songs_but_not_copy_them()
    {
        using var factory = new LibraryFactory(configured: true);
        var admin = await factory.CreateSuperAdminClientAsync();
        var playlist = await (await admin.PostAsJsonAsync("/api/playlists", new SavePlaylistRequest("NAS mix")))
            .Content.ReadFromJsonAsync<PlaylistDetailDto>(TestJson.Options);
        var nas = await (await admin.PostAsJsonAsync($"/api/playlists/{playlist!.Id}/tracks", new AddTrackRequest("nas:abc123", "Lofi Song")))
            .Content.ReadFromJsonAsync<TrackDto>(TestJson.Options);
        var yt = await (await admin.PostAsJsonAsync($"/api/playlists/{playlist.Id}/tracks", new AddTrackRequest("https://youtu.be/dQw4w9WgXcQ", "YT")))
            .Content.ReadFromJsonAsync<TrackDto>(TestJson.Options);

        var visitor = factory.CreateClient();
        var shared = await (await admin.PutAsJsonAsync($"/api/playlists/{playlist.Id}/share", new SharePlaylistRequest(true)))
            .Content.ReadFromJsonAsync<PlaylistDetailDto>(TestJson.Options);
        var code = shared!.ShareCode!;

        var link = await visitor.GetFromJsonAsync<LibraryStreamDto>($"/api/shared-playlists/{code}/tracks/{nas!.Id}/stream-url", TestJson.Options);
        var audio = await visitor.GetAsync(link!.Url);
        Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
        Assert.Equal("ID3-full-song", await audio.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/shared-playlists/{code}/tracks/{yt!.Id}/stream-url")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/shared-playlists/{code}/tracks/{Guid.NewGuid()}/stream-url")).StatusCode);

        // VI: Bản sao của người khác không có bài NAS. EN: Someone else's copy has no NAS songs.
        var user = await factory.CreateUserClientAsync();
        var copy = await (await user.PostAsync($"/api/shared-playlists/{code}/copy", null))
            .Content.ReadFromJsonAsync<PlaylistDetailDto>(TestJson.Options);
        Assert.Equal("YT", Assert.Single(copy!.Tracks).Title);

        // VI: Tắt chia sẻ thì khách hết lấy được link mới. EN: Unsharing stops new links.
        await admin.PutAsJsonAsync($"/api/playlists/{playlist.Id}/share", new SharePlaylistRequest(false));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/shared-playlists/{code}/tracks/{nas.Id}/stream-url")).StatusCode);
    }

    private static SaveProfileRequest ProfileWithSong(string slug, string song, bool isPublic = true) =>
        new(slug, "Test User", SongUrl: song, IsPublic: isPublic);

    [Fact]
    public async Task Admin_can_use_a_nas_song_on_the_bio_and_visitors_hear_only_that_song()
    {
        using var factory = new LibraryFactory(configured: true);
        var user = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.PutAsJsonAsync("/api/profile", ProfileWithSong("plainuser", "nas:abc123"))).StatusCode);

        var admin = await factory.CreateSuperAdminClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/profile", ProfileWithSong("nasowner", "nas:bad id"))).StatusCode);
        var saved = await admin.PutAsJsonAsync("/api/profile", ProfileWithSong("nasowner", "nas:abc123"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var visitor = factory.CreateClient();
        var profile = await visitor.GetFromJsonAsync<PublicProfileDto>("/api/profiles/nasowner?countView=false", TestJson.Options);
        Assert.Equal("nas:abc123", profile!.SongUrl);

        var link = await visitor.GetFromJsonAsync<LibraryStreamDto>("/api/profiles/nasowner/song/stream-url", TestJson.Options);
        Assert.Equal("Lofi Song · Chill", link!.Title);
        Assert.Contains("/api/library/songs/abc123/stream?token=", link.Url);
        Assert.Equal("ID3-full-song", await (await visitor.GetAsync(link.Url)).Content.ReadAsStringAsync());

        // VI: Hồ sơ riêng tư hoặc nhạc YouTube thì không có link NAS. EN: A private profile or a YouTube song gives no NAS link.
        await admin.PutAsJsonAsync("/api/profile", ProfileWithSong("nasowner", "nas:abc123", isPublic: false));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/api/profiles/nasowner/song/stream-url")).StatusCode);
        await admin.PutAsJsonAsync("/api/profile", ProfileWithSong("nasowner", "https://youtu.be/dQw4w9WgXcQ"));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/api/profiles/nasowner/song/stream-url")).StatusCode);
    }

    [Fact]
    public async Task Missing_song_is_not_found()
    {
        using var factory = new LibraryFactory(configured: true);
        var admin = await factory.CreateSuperAdminClientAsync();
        var link = await admin.GetFromJsonAsync<LibraryStreamDto>("/api/library/songs/missing/stream-url", TestJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(link!.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/library/songs/bad%20id/stream-url")).StatusCode);
    }
}
