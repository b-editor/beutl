namespace Beutl.Editor;

// Set while history replays or rolls back changes, and around writes it must not record; handlers that create
// recorded children skip rebuilding them while it is set, because history restores those children itself.
internal static class HistoryReplay
{
    private static readonly AsyncSuppressionCounter s_counter = new();

    public static bool IsActive => s_counter.IsSuppressed;

    public static IDisposable Enter() => s_counter.Enter();
}
