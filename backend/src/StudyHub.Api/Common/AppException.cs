namespace StudyHub.Api.Common;

// VI: Lỗi nghiệp vụ, được GlobalExceptionHandler đổi thành ProblemDetails với đúng mã HTTP.
// EN: Business errors, turned into ProblemDetails with the right HTTP status by GlobalExceptionHandler.
public class AppException(int statusCode, string title, string? detail = null) : Exception(detail ?? title)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
}

public class NotFoundException(string resource)
    : AppException(StatusCodes.Status404NotFound, "Not found", $"{resource} was not found.");

public class ConflictException(string detail)
    : AppException(StatusCodes.Status409Conflict, "Conflict", detail);

public class BadRequestException(string detail)
    : AppException(StatusCodes.Status400BadRequest, "Bad request", detail);

public class UnauthorizedException(string detail)
    : AppException(StatusCodes.Status401Unauthorized, "Unauthorized", detail);
