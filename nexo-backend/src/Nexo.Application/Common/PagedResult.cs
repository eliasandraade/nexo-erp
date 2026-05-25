namespace Nexo.Application.Common;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasNext   => Page < TotalPages;
    public bool HasPrev   => Page > 1;
}
