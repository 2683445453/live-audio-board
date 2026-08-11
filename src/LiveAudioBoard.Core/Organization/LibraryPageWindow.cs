namespace LiveAudioBoard.Core.Library;

public sealed record LibraryPageWindow(
    int CurrentPage,
    int TotalPages,
    int Skip,
    int Take,
    int TotalItems)
{
    public static LibraryPageWindow Create(
        int totalItems,
        int requestedPage,
        int pageSize)
    {
        var normalizedTotal = Math.Max(0, totalItems);
        var normalizedPageSize = Math.Max(1, pageSize);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(normalizedTotal / (double)normalizedPageSize));
        var currentPage = Math.Clamp(requestedPage, 1, totalPages);
        return new LibraryPageWindow(
            currentPage,
            totalPages,
            (currentPage - 1) * normalizedPageSize,
            normalizedPageSize,
            normalizedTotal);
    }
}
