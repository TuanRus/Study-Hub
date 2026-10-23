using System.Net;
using System.Net.Http.Json;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Tests;

public class PomodoroAndMusicTests(StudyHubFactory factory) : IClassFixture<StudyHubFactory>
{
    private static Task<HttpResponseMessage> PostJson(HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, TestJson.Options);

    [Fact]
    public async Task Work_sessions_count_toward_today_and_streak()
    {
        var client = await factory.CreateUserClientAsync();
        var end = DateTimeOffset.UtcNow.AddMinutes(-1);
        await PostJson(client, "/api/focus-sessions", new CreateFocusSessionRequest(SessionKind.Work, end.AddMinutes(-25), end));
        await PostJson(client, "/api/focus-sessions", new CreateFocusSessionRequest(SessionKind.ShortBreak, end.AddMinutes(-5), end));

        var stats = await client.GetFromJsonAsync<StatsSummaryDto>("/api/stats/summary?days=7", TestJson.Options);
        Assert.Equal(1, stats!.Today.Sessions);
        Assert.Equal(25, stats.Today.FocusMinutes);
        Assert.Equal(1, stats.CurrentStreak);
        Assert.Equal(7, stats.Days.Count);
    }

    [Fact]
    public async Task Future_session_is_rejected()
    {
        var client = await factory.CreateUserClientAsync();
        var start = DateTimeOffset.UtcNow.AddHours(1);
        var res = await PostJson(client, "/api/focus-sessions", new CreateFocusSessionRequest(SessionKind.Work, start, start.AddMinutes(25)));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Settings_can_be_updated()
    {
        var client = await factory.CreateUserClientAsync();
        var res = await client.PutAsJsonAsync("/api/pomodoro/settings", new PomodoroSettingsDto(50, 10, 20, 3, false, 180));
        res.EnsureSuccessStatusCode();
        var settings = await client.GetFromJsonAsync<PomodoroSettingsDto>("/api/pomodoro/settings", TestJson.Options);
        Assert.Equal(50, settings!.WorkMinutes);
    }

    [Fact]
    public async Task New_user_has_favorites_and_can_add_tracks_once()
    {
        var client = await factory.CreateUserClientAsync();
        var playlists = await client.GetFromJsonAsync<List<PlaylistDto>>("/api/playlists", TestJson.Options);
        var favorites = Assert.Single(playlists!);
        Assert.True(favorites.IsDefault);

        var add = await client.PostAsJsonAsync($"/api/playlists/{favorites.Id}/tracks",
            new AddTrackRequest("https://www.youtube.com/watch?v=jfKfPfyJRdk", "Lofi"));
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);

        var dup = await client.PostAsJsonAsync($"/api/playlists/{favorites.Id}/tracks",
            new AddTrackRequest("https://youtu.be/jfKfPfyJRdk"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/playlists/{favorites.Id}")).StatusCode);
    }

    [Fact]
    public async Task Import_is_idempotent()
    {
        var client = await factory.CreateUserClientAsync();
        var body = new ImportLocalRequest(
            [new LocalTodo(1729000000000, "Old todo", true), new LocalTodo(1792727203881, "Math homework", false, "2026-10-30", "20:00", "high")],
            [new LocalMusic("Lofi", "https://youtu.be/jfKfPfyJRdk"), new LocalMusic("Broken", "not a link")],
            new() { ["2026-10-20"] = new LocalFocusDay(2, 50) });

        var firstRes = await client.PostAsJsonAsync("/api/import/local", body);
        Assert.True(firstRes.IsSuccessStatusCode, await firstRes.Content.ReadAsStringAsync());
        var first = await firstRes.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(new ImportResultDto(2, 1, 1, 2), first);

        var tasks = await client.GetFromJsonAsync<List<TaskDto>>("/api/tasks?search=math", TestJson.Options);
        var homework = Assert.Single(tasks!);
        Assert.Equal(new DateTime(2026, 10, 30, 13, 0, 0, DateTimeKind.Utc), homework.EndAt);
        Assert.Equal(TaskPriority.High, homework.Priority);

        var second = await (await client.PostAsJsonAsync("/api/import/local", body)).Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(new ImportResultDto(0, 0, 2, 0), second);
    }
}
