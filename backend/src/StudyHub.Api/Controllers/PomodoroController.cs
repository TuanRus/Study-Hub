using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Authorize]
[Tags("Pomodoro & stats")]
public class PomodoroController(PomodoroService pomodoro, StatsService stats) : ControllerBase
{
    [HttpGet("api/pomodoro/settings")]
    public Task<PomodoroSettingsDto> GetSettings(CancellationToken ct) =>
        pomodoro.GetSettingsAsync(User.GetUserId(), ct);

    [HttpPut("api/pomodoro/settings")]
    public Task<PomodoroSettingsDto> UpdateSettings(PomodoroSettingsDto req, CancellationToken ct) =>
        pomodoro.UpdateSettingsAsync(User.GetUserId(), req, ct);

    /// <summary>Record a finished (or skipped) Pomodoro phase. Optionally link it to a task.</summary>
    [HttpPost("api/focus-sessions")]
    [ProducesResponseType<FocusSessionDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> LogSession(CreateFocusSessionRequest req, CancellationToken ct)
    {
        var session = await pomodoro.LogSessionAsync(User.GetUserId(), req, ct);
        return StatusCode(StatusCodes.Status201Created, session);
    }

    /// <summary>Sessions that started in [from, to). Defaults to the last 7 days.</summary>
    [HttpGet("api/focus-sessions")]
    public Task<List<FocusSessionDto>> ListSessions(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] SessionKind? kind, CancellationToken ct) =>
        pomodoro.ListSessionsAsync(User.GetUserId(), from, to, kind, ct);

    [HttpDelete("api/focus-sessions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSession(Guid id, CancellationToken ct)
    {
        await pomodoro.DeleteSessionAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    /// <summary>Focus time per day for the last N days, today's progress, streaks and task counts.</summary>
    [HttpGet("api/stats/summary")]
    public Task<StatsSummaryDto> Summary(
        [FromQuery, Range(1, 365)] int days = 7, [FromQuery] string? timeZone = null, CancellationToken ct = default) =>
        stats.SummaryAsync(User.GetUserId(), days, timeZone, ct);
}
