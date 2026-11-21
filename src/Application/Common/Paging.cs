namespace Application.Common;

public static class Paging
{
    public const int MaxPageSize = 200;
    public const int MaxPage = 1_000_000;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Clamp(page, 1, MaxPage), Math.Clamp(pageSize, 1, MaxPageSize));
}
