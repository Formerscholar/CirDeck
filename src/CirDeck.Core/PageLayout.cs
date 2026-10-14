namespace CirDeck.Core;

public static class PageLayout
{
    public static int PageSize(int columns, int rows)
    {
        return Math.Max(1, columns) * Math.Max(1, rows);
    }

    public static int PageCount(int items, int pageSize)
    {
        if (items <= 0 || pageSize <= 0)
        {
            return 1;
        }

        return (items + pageSize - 1) / pageSize;
    }

    public static int Clamp(int page, int pageCount)
    {
        if (pageCount <= 1)
        {
            return 0;
        }

        if (page < 0)
        {
            return 0;
        }

        if (page >= pageCount)
        {
            return pageCount - 1;
        }

        return page;
    }
}
