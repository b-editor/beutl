namespace Beutl.Serialization;

// Records actual serialized identities, including objects reached through custom converters.
internal sealed class SerializedObjectCapture : IDisposable
{
    [ThreadStatic]
    private static SerializedObjectCapture? t_current;
    private readonly SerializedObjectCapture? _parent = t_current;
    private readonly HashSet<CoreObject> _objects = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public SerializedObjectCapture() => t_current = this;
    public IEnumerable<CoreObject> Objects => _objects;

    public static void Record(ICoreSerializable value)
    {
        if (value is not CoreObject obj) return;
        for (SerializedObjectCapture? capture = t_current; capture != null; capture = capture._parent)
            capture._objects.Add(obj);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!ReferenceEquals(t_current, this))
            throw new InvalidOperationException("Serialized object captures must be disposed in stack order.");
        t_current = _parent;
        _disposed = true;
    }
}
