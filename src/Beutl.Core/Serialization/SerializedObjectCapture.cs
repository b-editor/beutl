namespace Beutl.Serialization;

// Records actual serialized identities, including objects reached through custom converters.
internal sealed class SerializedObjectCapture : IDisposable
{
    [ThreadStatic]
    private static SerializedObjectCapture? t_current;
    private readonly SerializedObjectCapture? _parent = t_current;
    private readonly HashSet<CoreObject> _objects = new(ReferenceEqualityComparer.Instance);
    private HashSet<IFallback>? _fallbacks;
    private bool _disposed;

    public SerializedObjectCapture() => t_current = this;
    public IEnumerable<CoreObject> Objects => _objects;

    // Whether fallback was serialized, which writes the JSON it was read from back.
    public bool Contains(IFallback fallback) => _fallbacks?.Contains(fallback) == true;

    public static void Record(ICoreSerializable value)
    {
        for (SerializedObjectCapture? capture = t_current; capture != null; capture = capture._parent)
        {
            if (value is CoreObject obj) capture._objects.Add(obj);
            if (value is IFallback fallback)
                (capture._fallbacks ??= new HashSet<IFallback>(ReferenceEqualityComparer.Instance)).Add(fallback);
        }
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
