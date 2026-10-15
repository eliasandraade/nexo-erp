namespace Nexo.Application.Modules.Build;

internal static class BuildPaging
{
    public const int MaxPageSize = 100;

    /// <summary>page ≥ 1 and 1 ≤ pageSize ≤ 100 — a client can't ask for the whole table at once.</summary>
    public static (int Page, int PageSize) Clamp(int page, int pageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
}
