namespace Beutl.Animation;

// Observe the serialized graph, including keyframes hidden inside plugin-owned wrappers.
internal sealed class LossyEasingSerializationCapture : IDisposable
{
    [ThreadStatic]
    private static LossyEasingSerializationCapture? t_current;

    private readonly LossyEasingSerializationCapture? _parent = t_current;
    private bool _disposed;

    public LossyEasingSerializationCapture() => t_current = this;

    public bool HasLossyEasing { get; private set; }

    public static void Record()
    {
        for (LossyEasingSerializationCapture? capture = t_current; capture != null; capture = capture._parent)
            capture.HasLossyEasing = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!ReferenceEquals(t_current, this))
            throw new InvalidOperationException("Lossy easing captures must be disposed in stack order.");
        t_current = _parent;
        _disposed = true;
    }
}
