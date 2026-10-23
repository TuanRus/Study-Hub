using StudyHub.Api.Common;
using StudyHub.Api.Entities;
using StudyHub.Api.Services;

namespace StudyHub.Api.Tests;

public class MusicLinkResolverTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=jfKfPfyJRdk&t=10", "https://www.youtube.com/embed/jfKfPfyJRdk")]
    [InlineData("https://youtu.be/jfKfPfyJRdk?si=abc", "https://www.youtube.com/embed/jfKfPfyJRdk")]
    [InlineData("https://m.youtube.com/shorts/jfKfPfyJRdk", "https://www.youtube.com/embed/jfKfPfyJRdk")]
    [InlineData("https://www.youtube.com/playlist?list=PLx0sYbCqOb8TBPRdmBHs5Iftvv9TPboYG",
        "https://www.youtube.com/embed/videoseries?list=PLx0sYbCqOb8TBPRdmBHs5Iftvv9TPboYG")]
    [InlineData("https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC?si=1", "https://open.spotify.com/embed/track/4uLU6hMCjMI75M1A2tKUQC")]
    [InlineData("https://open.spotify.com/intl-vi/playlist/37i9dQZF1DXcBWIGoYBM5M", "https://open.spotify.com/embed/playlist/37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("spotify:album:4aawyAB9vmqN3uQ7FjRGTy", "https://open.spotify.com/embed/album/4aawyAB9vmqN3uQ7FjRGTy")]
    public void Resolves_supported_links(string input, string expected) =>
        Assert.Equal(expected, MusicLinkResolver.Resolve(input).EmbedUrl);

    [Theory]
    [InlineData("not a url")]
    [InlineData("https://vimeo.com/123")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    public void Rejects_unsupported_links(string input) =>
        Assert.Throws<BadRequestException>(() => MusicLinkResolver.Resolve(input));

    [Fact]
    public void Detects_provider() =>
        Assert.Equal(MusicProvider.Spotify, MusicLinkResolver.Resolve("https://open.spotify.com/episode/4uLU6hMCjMI75M1A2tKUQC").Provider);
}

public class StreakTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);

    [Fact]
    public void Counts_consecutive_days_ending_today()
    {
        var days = new HashSet<DateOnly> { Today, Today.AddDays(-1), Today.AddDays(-2), Today.AddDays(-5) };
        Assert.Equal((3, 3), StatsService.Streaks(days, Today));
    }

    [Fact]
    public void Streak_survives_until_today_ends()
    {
        var days = new HashSet<DateOnly> { Today.AddDays(-1), Today.AddDays(-2) };
        Assert.Equal((2, 2), StatsService.Streaks(days, Today));
    }

    [Fact]
    public void Longest_streak_can_be_in_the_past()
    {
        var days = new HashSet<DateOnly> { Today, Today.AddDays(-10), Today.AddDays(-11), Today.AddDays(-12), Today.AddDays(-13) };
        Assert.Equal((1, 4), StatsService.Streaks(days, Today));
    }
}
