using System.Net;
using System.Net.Http.Json;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Tests;

public class ProfileTests(StudyHubFactory factory) : IClassFixture<StudyHubFactory>
{
    private static string NewName() => "p" + Guid.NewGuid().ToString("N")[..10];

    private static SaveProfileRequest Request(string slug, Dictionary<string, string>? links = null, bool isPublic = true) =>
        new(slug, "Test User", "https://example.com/a.png", "line 1\nline 2", "Vietnam", ["student", "coder"],
            "ocean", null, "snow", "#FF0066", "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            links ?? new() { ["github"] = "https://github.com/someone" }, isPublic);

    private async Task<PublicProfileDto> Visit(string slug, string? visitorId, bool countView = true, HttpClient? client = null)
    {
        client ??= factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/profiles/{slug}?countView={countView}");
        if (visitorId is not null) req.Headers.Add("X-Visitor-Id", visitorId);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PublicProfileDto>(TestJson.Options))!;
    }

    [Fact]
    public async Task New_user_gets_a_profile_named_after_the_username()
    {
        var name = NewName();
        var client = await factory.CreateUserClientAsync(name);
        var profile = await client.GetFromJsonAsync<ProfileDto>("/api/profile", TestJson.Options);
        Assert.Equal(name, profile!.Slug);
        Assert.Equal(0, profile.ViewCount);
        Assert.Null(profile.UpdatedAt);
        Assert.True(profile.IsPublic);
    }

    [Fact]
    public async Task Profile_can_be_saved_and_read_publicly_without_signing_in()
    {
        var client = await factory.CreateUserClientAsync();
        var slug = NewName();
        var res = await client.PutAsJsonAsync("/api/profile", Request(slug.ToUpperInvariant()), TestJson.Options);
        res.EnsureSuccessStatusCode();

        var visitor = await Visit(slug, Guid.NewGuid().ToString());
        Assert.Equal(slug, visitor.Slug);
        Assert.Equal("Test User", visitor.DisplayName);
        Assert.Equal("#ff0066", visitor.AccentColor);
        Assert.Equal(["student", "coder"], visitor.Badges);
        Assert.Equal("https://github.com/someone", visitor.Links["github"]);
        Assert.False(visitor.IsOwner);
    }

    [Fact]
    public async Task Views_count_once_per_visitor_per_day_and_never_for_the_owner()
    {
        var name = NewName();
        var owner = await factory.CreateUserClientAsync(name);
        var alice = Guid.NewGuid().ToString();

        Assert.Equal(1, (await Visit(name, alice)).ViewCount);
        Assert.Equal(1, (await Visit(name, alice)).ViewCount);
        Assert.Equal(2, (await Visit(name, Guid.NewGuid().ToString())).ViewCount);
        Assert.Equal(2, (await Visit(name, Guid.NewGuid().ToString(), countView: false)).ViewCount);

        var own = await Visit(name, Guid.NewGuid().ToString(), client: owner);
        Assert.True(own.IsOwner);
        Assert.Equal(2, own.ViewCount);

        var mine = await owner.GetFromJsonAsync<ProfileDto>("/api/profile", TestJson.Options);
        Assert.Equal(2, mine!.ViewCount);
    }

    [Fact]
    public async Task One_ip_counts_a_limited_number_of_views_per_day()
    {
        var name = NewName();
        await factory.CreateUserClientAsync(name);
        PublicProfileDto last = null!;
        for (var i = 0; i < 25; i++)
            last = await Visit(name, Guid.NewGuid().ToString());
        Assert.Equal(20, last.ViewCount);
    }

    [Fact]
    public async Task Visitors_without_an_id_are_deduplicated_by_ip_and_browser()
    {
        var name = NewName();
        await factory.CreateUserClientAsync(name);
        await Visit(name, null);
        Assert.Equal(1, (await Visit(name, null)).ViewCount);
    }

    [Fact]
    public async Task Private_profile_is_hidden_from_visitors_but_not_the_owner()
    {
        var owner = await factory.CreateUserClientAsync();
        var slug = NewName();
        (await owner.PutAsJsonAsync("/api/profile", Request(slug, isPublic: false), TestJson.Options)).EnsureSuccessStatusCode();

        var anon = await factory.CreateClient().GetAsync($"/api/profiles/{slug}");
        Assert.Equal(HttpStatusCode.NotFound, anon.StatusCode);
        Assert.True((await Visit(slug, null, client: owner)).IsOwner);
    }

    [Fact]
    public async Task Unknown_profile_returns_404()
    {
        var res = await factory.CreateClient().GetAsync("/api/profiles/nobody-here-123");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Slug_must_be_unique()
    {
        var takenName = NewName();
        await factory.CreateUserClientAsync(takenName);
        var other = await factory.CreateUserClientAsync();

        var res = await other.PutAsJsonAsync("/api/profile", Request(takenName), TestJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        var check = await other.GetFromJsonAsync<SlugAvailabilityDto>($"/api/profile/slug-available?slug={takenName}", TestJson.Options);
        Assert.False(check!.Available);
    }

    [Fact]
    public async Task LinkedIn_link_is_saved_and_shown_publicly()
    {
        var client = await factory.CreateUserClientAsync();
        var slug = NewName();
        var links = new Dictionary<string, string> { ["LinkedIn"] = "https://www.linkedin.com/in/someone" };
        var res = await client.PutAsJsonAsync("/api/profile", Request(slug, links), TestJson.Options);
        res.EnsureSuccessStatusCode();

        var visitor = await Visit(slug, Guid.NewGuid().ToString());
        Assert.Equal("https://www.linkedin.com/in/someone", visitor.Links["linkedin"]);
    }

    // VI: PNG 1x1 thật, dạng base64. EN: A real 1x1 PNG in base64.
    private const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Avatar_uploaded_from_the_device_is_saved_and_shown_publicly()
    {
        var client = await factory.CreateUserClientAsync();
        var slug = NewName();
        var res = await client.PutAsJsonAsync("/api/profile", Request(slug) with { AvatarUrl = TinyPng }, TestJson.Options);
        res.EnsureSuccessStatusCode();
        Assert.Equal(TinyPng, (await Visit(slug, Guid.NewGuid().ToString())).AvatarUrl);
    }

    [Theory]
    [InlineData("data:image/png;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+")]   // "<svg onload=alert(1)>" labelled as PNG
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]           // SVG is not allowed
    [InlineData("data:text/html;base64,PGgxPmhpPC9oMT4=")]
    [InlineData("javascript:alert(1)")]
    public async Task Bad_avatar_images_are_rejected(string avatar)
    {
        var client = await factory.CreateUserClientAsync();
        var res = await client.PutAsJsonAsync("/api/profile", Request(NewName()) with { AvatarUrl = avatar }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Avatar_image_that_is_too_large_is_rejected()
    {
        var client = await factory.CreateUserClientAsync();
        var big = TinyPng + new string('A', Profile.MaxAvatarLength);
        var res = await client.PutAsJsonAsync("/api/profile", Request(NewName()) with { AvatarUrl = big }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("github", "javascript:alert(1)")]
    [InlineData("linkedin", "ftp://linkedin.com/in/someone")]
    [InlineData("myspace", "https://example.com")]
    public async Task Bad_links_are_rejected(string type, string url)
    {
        var client = await factory.CreateUserClientAsync();
        var res = await client.PutAsJsonAsync("/api/profile", Request(NewName(), new() { [type] = url }), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Song_must_be_a_youtube_or_spotify_link()
    {
        var client = await factory.CreateUserClientAsync();
        var res = await client.PutAsJsonAsync("/api/profile",
            Request(NewName()) with { SongUrl = "https://example.com/song.mp3" }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Public_profile_shows_real_study_stats()
    {
        var name = NewName();
        var client = await factory.CreateUserClientAsync(name);
        var end = DateTimeOffset.UtcNow.AddMinutes(-1);
        await client.PostAsJsonAsync("/api/focus-sessions",
            new CreateFocusSessionRequest(SessionKind.Work, end.AddMinutes(-25), end), TestJson.Options);
        await client.PostAsJsonAsync("/api/tasks", new SaveTaskRequest("Done already", IsCompleted: true), TestJson.Options);

        var stats = (await Visit(name, null)).Stats;
        Assert.Equal(25, stats.TotalFocusMinutes);
        Assert.Equal(25, stats.WeekFocusMinutes);
        Assert.Equal(1, stats.TasksCompleted);
        Assert.Equal(1, stats.CurrentStreak);
        Assert.NotNull(stats.LastFocusAt);
    }

    [Fact]
    public async Task Local_profile_is_imported_only_while_untouched()
    {
        var client = await factory.CreateUserClientAsync();
        var handle = NewName();
        var local = new LocalProfile("Local Test User", "@" + handle, "javascript:bad", "hi", "VN", ["coder", "bogus"],
            "sunset", null, "none", "#123456", "https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC",
            new() { ["facebook"] = "https://example.com/facebook/testuser", ["github"] = "not a url" });

        var first = await (await client.PostAsJsonAsync("/api/import/local",
            new ImportLocalRequest(null, null, null, local), TestJson.Options)).Content.ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        Assert.True(first!.ProfileImported);

        var profile = await client.GetFromJsonAsync<ProfileDto>("/api/profile", TestJson.Options);
        Assert.Equal(handle, profile!.Slug);
        Assert.Equal("Local Test User", profile.DisplayName);
        Assert.Null(profile.AvatarUrl);
        Assert.Equal(["coder"], profile.Badges);
        Assert.Equal("sunset", profile.Background);
        Assert.Single(profile.Links);

        var second = await (await client.PostAsJsonAsync("/api/import/local",
            new ImportLocalRequest(null, null, null, local with { Name = "Again" }), TestJson.Options)).Content.ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        Assert.False(second!.ProfileImported);
    }

    [Fact]
    public async Task Frontend_is_served_for_profile_links_without_exposing_the_backend()
    {
        var client = factory.CreateClient();

        var page = await client.GetAsync("/@someone");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("js/main.js", await page.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/js/main.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/backend/src/StudyHub.Api/appsettings.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/readme.md")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/js/../backend/StudyHub.slnx")).StatusCode);
    }
}
