namespace Beutl.Editor;

// IOperationObserverの通知を抑制
public static class PublishingSuppression
{
    private static readonly AsyncSuppressionCounter s_suppression = new();

    public static bool IsSuppressed => s_suppression.IsSuppressed;

    public static IDisposable Enter() => s_suppression.Enter();
}
