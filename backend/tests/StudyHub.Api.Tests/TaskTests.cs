using System.Net;
using System.Net.Http.Json;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Tests;

public class TaskTests(StudyHubFactory factory) : IClassFixture<StudyHubFactory>
{
    private static async Task<TaskDto> CreateAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/tasks", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<TaskDto>(TestJson.Options))!;
    }

    [Fact]
    public async Task Timed_task_is_stored_in_utc()
    {
        var client = await factory.CreateUserClientAsync();
        var task = await CreateAsync(client, new
        {
            title = "Study group",
            startAt = "2026-11-02T14:00:00+07:00",
            endAt = "2026-11-02T16:00:00+07:00",
            color = "#3366FF"
        });
        Assert.Equal(new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc), task.StartAt);
        Assert.Equal("#3366ff", task.Color);
    }

    [Fact]
    public async Task All_day_task_keeps_the_calendar_date()
    {
        var client = await factory.CreateUserClientAsync();
        var task = await CreateAsync(client, new
        {
            title = "Exam week",
            startAt = "2026-11-09T00:00:00+07:00",
            endAt = "2026-11-13T00:00:00+07:00",
            isAllDay = true
        });
        Assert.Equal(new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), task.StartAt);
        Assert.Equal(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), task.EndAt);
    }

    [Fact]
    public async Task Calendar_returns_tasks_overlapping_the_range_only()
    {
        var client = await factory.CreateUserClientAsync();
        await CreateAsync(client, new { title = "In range", endAt = "2026-12-10T10:00:00+07:00" });
        await CreateAsync(client, new { title = "Spans into range", startAt = "2026-11-28", endAt = "2026-12-02", isAllDay = true });
        await CreateAsync(client, new { title = "Outside", endAt = "2027-01-05T10:00:00+07:00" });
        await CreateAsync(client, new { title = "No date" });

        var cal = await client.GetFromJsonAsync<CalendarResponse>(
            "/api/tasks/calendar?from=2026-12-01&to=2026-12-31&timeZone=Asia/Ho_Chi_Minh", TestJson.Options);

        Assert.Equal(["Spans into range", "In range"], cal!.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Past_deadline_is_overdue_until_completed()
    {
        var client = await factory.CreateUserClientAsync();
        var task = await CreateAsync(client, new { title = "Late", endAt = DateTimeOffset.UtcNow.AddHours(-1) });
        Assert.True(task.IsOverdue);

        var overdue = await client.GetFromJsonAsync<List<TaskDto>>("/api/tasks?status=Overdue", TestJson.Options);
        Assert.Single(overdue!);

        var done = await client.PatchAsJsonAsync($"/api/tasks/{task.Id}/completion", new SetCompletionRequest(true));
        var updated = await done.Content.ReadFromJsonAsync<TaskDto>(TestJson.Options);
        Assert.False(updated!.IsOverdue);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task End_before_start_is_rejected()
    {
        var client = await factory.CreateUserClientAsync();
        var res = await client.PostAsJsonAsync("/api/tasks",
            new { title = "Bad", startAt = "2026-11-02T16:00:00Z", endAt = "2026-11-02T14:00:00Z" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_tasks()
    {
        var owner = await factory.CreateUserClientAsync();
        var other = await factory.CreateUserClientAsync();
        var task = await CreateAsync(owner, new { title = "Private", priority = nameof(TaskPriority.High) });

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<TaskDto>>("/api/tasks", TestJson.Options))!);
    }
}
