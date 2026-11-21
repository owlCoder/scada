namespace Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> From(IEnumerable<T> source, int page, int pageSize)
    {
        var all = source.ToArray();
        var (p, size) = Paging.Normalize(page, pageSize);
        return new PagedResult<T>(all.Skip((p - 1) * size).Take(size).ToArray(), p, size, all.Length);
    }
}
