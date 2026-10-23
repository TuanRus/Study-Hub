using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace StudyHub.Api.Common;

public class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            AppException app => new ProblemDetails { Status = app.StatusCode, Title = app.Title, Detail = app.Detail },
            _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Server error" }
        };

        if (exception is not AppException)
            logger.LogError(exception, "Unhandled exception");

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
