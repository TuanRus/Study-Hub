using System.Net;
using System.Net.Http.Json;
using StudyHub.Api.Dtos;

namespace StudyHub.Api.Tests;

public class PlaylistTests(StudyHubFactory factory) : IClassFixture<StudyHubFactory>
{
    private static async Task<PlaylistDetailDto> CreatePlaylist(HttpClient client, string name, params string[] urls)
    {
        var res = await client.PostAsJsonAsync("/api/playlists", new SavePlaylistRequest(name));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDetailDto>(TestJson.Options))!;
        foreach (var url in urls)
            (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/tracks", new AddTrackRequest(url))).EnsureSuccessStatusCode();
        return playlist;
    }

    private static async Task<PlaylistDetailDto> Share(HttpClient client, Guid id, bool isShared)
    {
        var res = await client.PutAsJsonAsync($"/api/playlists/{id}/share", new SharePlaylistRequest(isShared));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDetailDto>(TestJson.Options))!;
    }

    [Fact]
    public async Task Shared_playlist_is_visible_without_login_and_keeps_track_order()
    {
        var owner = await factory.CreateUserClientAsync();
        var playlist = await CreatePlaylist(owner, "Lofi study",
            "https://www.youtube.com/watch?v=jfKfPfyJRdk", "https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT");

        Assert.False(playlist.IsShared);
        Assert.Null(playlist.ShareCode);

        var shared = await Share(owner, playlist.Id, true);
        Assert.True(shared.IsShared);
        Assert.Matches("^[A-Za-z0-9]{10}$", shared.ShareCode!);
        // VI: Bật lại không đổi link. EN: Sharing again keeps the same link.
        Assert.Equal(shared.ShareCode, (await Share(owner, playlist.Id, true)).ShareCode);

        var list = await owner.GetFromJsonAsync<List<PlaylistDto>>("/api/playlists", TestJson.Options);
        Assert.Contains(list!, p => p.Id == playlist.Id && p.IsShared && p.ShareCode == shared.ShareCode && p.TrackCount == 2);

        var anonymous = factory.CreateClient();
        var view = await anonymous.GetFromJsonAsync<SharedPlaylistDto>($"/api/shared-playlists/{shared.ShareCode}", TestJson.Options);
        Assert.Equal("Lofi study", view!.Name);
        Assert.Equal(2, view.Tracks.Count);
        Assert.Contains("youtube.com/embed/jfKfPfyJRdk", view.Tracks[0].EmbedUrl);
        Assert.NotNull(view.OwnerSlug);
    }

    [Fact]
    public async Task Unsharing_breaks_the_old_link_and_resharing_makes_a_new_one()
    {
        var owner = await factory.CreateUserClientAsync();
        var playlist = await CreatePlaylist(owner, "Focus", "https://youtu.be/jfKfPfyJRdk");
        var first = (await Share(owner, playlist.Id, true)).ShareCode!;

        var off = await Share(owner, playlist.Id, false);
        Assert.False(off.IsShared);
        Assert.Null(off.ShareCode);
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shared-playlists/{first}")).StatusCode);

        var second = (await Share(owner, playlist.Id, true)).ShareCode!;
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/shared-playlists/{second}")).StatusCode);
    }

    [Fact]
    public async Task Unknown_codes_and_other_users_playlists_are_not_found()
    {
        var owner = await factory.CreateUserClientAsync();
        var other = await factory.CreateUserClientAsync();
        var playlist = await CreatePlaylist(owner, "Mine");

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/shared-playlists/abcdefghij")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/shared-playlists/x")).StatusCode);
        var res = await other.PutAsJsonAsync($"/api/playlists/{playlist.Id}/share", new SharePlaylistRequest(true));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync($"/api/playlists/{playlist.Id}/share", new SharePlaylistRequest(true))).StatusCode);
    }

    [Fact]
    public async Task A_shared_playlist_can_be_copied_and_the_copy_is_private()
    {
        var owner = await factory.CreateUserClientAsync();
        var playlist = await CreatePlaylist(owner, "Chill mix",
            "https://youtu.be/jfKfPfyJRdk", "https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT");
        var code = (await Share(owner, playlist.Id, true)).ShareCode!;

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/shared-playlists/{code}/copy", null)).StatusCode);

        var friend = await factory.CreateUserClientAsync();
        var res = await friend.PostAsync($"/api/shared-playlists/{code}/copy", null);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var copy = (await res.Content.ReadFromJsonAsync<PlaylistDetailDto>(TestJson.Options))!;
        Assert.Equal("Chill mix", copy.Name);
        Assert.False(copy.IsShared);
        Assert.Equal(2, copy.Tracks.Count);
        Assert.Equal([0, 1], copy.Tracks.Select(t => t.Position));

        // VI: Bản sao độc lập: xóa bài ở bản sao không ảnh hưởng bản gốc.
        // EN: The copy is independent: removing a track from it leaves the original alone.
        (await friend.DeleteAsync($"/api/playlists/{copy.Id}/tracks/{copy.Tracks[0].Id}")).EnsureSuccessStatusCode();
        var original = await anonymous.GetFromJsonAsync<SharedPlaylistDto>($"/api/shared-playlists/{code}", TestJson.Options);
        Assert.Equal(2, original!.Tracks.Count);
    }

    [Fact]
    public async Task Playlists_of_a_locked_account_are_hidden()
    {
        var owner = await factory.CreateUserClientAsync();
        var me = await owner.GetFromJsonAsync<UserDto>("/api/auth/me", TestJson.Options);
        var playlist = await CreatePlaylist(owner, "Soon hidden", "https://youtu.be/jfKfPfyJRdk");
        var code = (await Share(owner, playlist.Id, true)).ShareCode!;

        var super = await factory.CreateSuperAdminClientAsync();
        (await super.PutAsJsonAsync($"/api/admin/users/{me!.Id}/status", new SetUserStatusRequest(true))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/shared-playlists/{code}")).StatusCode);
    }

    [Fact]
    public async Task Playlist_count_is_capped()
    {
        var client = await factory.CreateUserClientAsync();
        // VI: Favorites đã chiếm một chỗ. EN: Favorites already takes one slot.
        for (var i = 1; i < StudyHub.Api.Services.PlaylistService.MaxPlaylistsPerUser; i++)
            await CreatePlaylist(client, $"List {i}");
        var res = await client.PostAsJsonAsync("/api/playlists", new SavePlaylistRequest("One too many"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Local_playlists_are_imported_once_and_merged_by_name()
    {
        var client = await factory.CreateUserClientAsync();
        var body = new ImportLocalRequest(null, null, null, null,
            [
                new LocalPlaylist("Night study", [new LocalMusic("Lofi", "https://youtu.be/jfKfPfyJRdk"), new LocalMusic("Bad", "nope")]),
                new LocalPlaylist("  ", [new LocalMusic("Lofi", "https://youtu.be/jfKfPfyJRdk")])
            ]);

        var first = await (await client.PostAsJsonAsync("/api/import/local", body)).Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(1, first!.PlaylistsImported);
        Assert.Equal(1, first.TracksImported);
        Assert.Equal(1, first.TracksSkipped);

        var second = await (await client.PostAsJsonAsync("/api/import/local", body)).Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(0, second!.PlaylistsImported);
        Assert.Equal(0, second.TracksImported);

        var playlists = await client.GetFromJsonAsync<List<PlaylistDto>>("/api/playlists", TestJson.Options);
        Assert.Equal(2, playlists!.Count);
        Assert.Contains(playlists, p => p.Name == "Night study" && p.TrackCount == 1);
    }
}
