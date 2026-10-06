namespace Beutl.Editor;

// HistoryMangerが記録するのを抑制
public static class RecordingSuppression
{
    private static readonly AsyncSuppressionCounter s_suppression = new();

    public static bool IsSuppressed => s_suppression.IsSuppressed;

    public static IDisposable Enter() => s_suppression.Enter();
}
