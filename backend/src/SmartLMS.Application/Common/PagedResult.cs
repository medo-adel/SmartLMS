namespace SmartLMS.Application.Common;

/// <summary>Every list endpoint returns this shape inside ApiResponse.Data.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
