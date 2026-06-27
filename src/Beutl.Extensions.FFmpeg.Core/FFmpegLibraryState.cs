namespace Beutl.Extensions.FFmpeg;

public static class FFmpegLibraryState
{
    private static volatile bool s_librariesMissing;

    public static event EventHandler? LibrariesMissing;

    public static bool IsLibrariesMissing => s_librariesMissing;

    public static void NotifyMissing()
    {
        s_librariesMissing = true;
        LibrariesMissing?.Invoke(null, EventArgs.Empty);
    }

    public static void MarkInstalled()
    {
        s_librariesMissing = false;
    }

    public static void MarkMissing()
    {
        s_librariesMissing = true;
    }
}
