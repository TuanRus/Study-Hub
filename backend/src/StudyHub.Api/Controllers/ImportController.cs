using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudyHub.Api.Common;
using StudyHub.Api.Dtos;
using StudyHub.Api.Services;

namespace StudyHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/import")]
[Tags("Import")]
public class ImportController(ImportService import) : ControllerBase
{
    /// <summary>Upload the data the app kept in localStorage before it had a backend. Safe to call more than once.</summary>
    [HttpPost("local")]
    public Task<ImportResultDto> ImportLocal(ImportLocalRequest req, CancellationToken ct) =>
        import.ImportAsync(User.GetUserId(), req, ct);
}
