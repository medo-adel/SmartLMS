namespace SmartLMS.Application.Common;

/// <summary>Standard envelope returned by every API endpoint.</summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    /// <summary>Stable machine-readable error code such as DEVICE_LIMIT_REACHED. Null on success.</summary>
    public string? Code { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null, string? code = null) =>
        new() { Success = false, Message = message, Code = code, Errors = errors?.ToArray() ?? [] };
}
