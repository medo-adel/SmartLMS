namespace SmartLMS.Application.Common;

public sealed record PagedQuery(int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;

    public int NormalizedPage => Math.Max(1, Page);
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}
