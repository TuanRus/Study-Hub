using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks")]
[Tags("Tasks & deadlines")]
public class TasksController(TaskService tasks) : ControllerBase
{
    /// <summary>List tasks. Filter by status (All, Active, Completed, Overdue), text, or whether they are on the calendar.</summary>
    [HttpGet]
    public Task<List<TaskDto>> List([FromQuery] TaskQuery query, [FromQuery] string? timeZone, CancellationToken ct) =>
        tasks.ListAsync(User.GetUserId(), query, timeZone, ct);

    /// <summary>Tasks shown on the calendar between two local dates (inclusive). Defaults to the current month.</summary>
    [HttpGet("calendar")]
    public Task<CalendarResponse> Calendar(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? timeZone, CancellationToken ct) =>
        tasks.CalendarAsync(User.GetUserId(), from, to, timeZone, ct);

    /// <summary>Unfinished tasks that are overdue or due within the next N days.</summary>
    [HttpGet("upcoming")]
    public Task<List<TaskDto>> Upcoming(
        [FromQuery, Range(1, 90)] int days = 7, [FromQuery] string? timeZone = null, CancellationToken ct = default) =>
        tasks.UpcomingAsync(User.GetUserId(), days, timeZone, ct);

    [HttpGet("{id:guid}")]
    public Task<TaskDto> Get(Guid id, CancellationToken ct) => tasks.GetAsync(User.GetUserId(), id, ct);

    [HttpPost]
    [ProducesResponseType<TaskDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveTaskRequest req, CancellationToken ct)
    {
        var task = await tasks.CreateAsync(User.GetUserId(), req, ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, task);
    }

    /// <summary>Replace every field of a task.</summary>
    [HttpPut("{id:guid}")]
    public Task<TaskDto> Update(Guid id, SaveTaskRequest req, CancellationToken ct) =>
        tasks.UpdateAsync(User.GetUserId(), id, req, ct);

    /// <summary>Move or resize a task on the calendar (drag and drop). Send nulls to unschedule.</summary>
    [HttpPatch("{id:guid}/schedule")]
    public Task<TaskDto> Schedule(Guid id, ScheduleTaskRequest req, CancellationToken ct) =>
        tasks.ScheduleAsync(User.GetUserId(), id, req, ct);

    [HttpPatch("{id:guid}/completion")]
    public Task<TaskDto> SetCompletion(Guid id, SetCompletionRequest req, CancellationToken ct) =>
        tasks.SetCompletionAsync(User.GetUserId(), id, req.IsCompleted, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tasks.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
