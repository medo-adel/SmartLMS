namespace SmartLMS.Domain.Exceptions;

/// <summary>An error with a stable machine-readable code and HTTP status, returned to clients in ApiResponse.Code.</summary>
public class AppException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class UnauthorizedAppException(string code = "UNAUTHORIZED", string message = "Authentication failed.") : AppException(401, code, message);

public sealed class ForbiddenAppException(string code, string message) : AppException(403, code, message);

public sealed class ConflictAppException(string code, string message) : AppException(409, code, message);

public sealed class ValidationAppException(string message, IEnumerable<string>? errors = null) : AppException(400, "VALIDATION_FAILED", message)
{
    public IReadOnlyList<string> Errors { get; } = errors?.ToArray() ?? [];
}
