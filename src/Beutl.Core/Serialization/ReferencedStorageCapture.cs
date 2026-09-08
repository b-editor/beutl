namespace Beutl.Serialization;

// Captures serializer reads independently of the shape of plugin-owned object graphs.
internal sealed class ReferencedStorageCapture : IDisposable
{
    [ThreadStatic]
    private static ReferencedStorageCapture? t_current;

    private readonly ReferencedStorageCapture? _parent = t_current;
    private readonly List<Uri> _sources = [];
    private bool _disposed;

    public ReferencedStorageCapture() => t_current = this;

    public IReadOnlyList<Uri> Sources => _sources;

    public static void Record(Uri uri)
    {
        for (ReferencedStorageCapture? capture = t_current; capture != null; capture = capture._parent)
            capture._sources.Add(uri);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!ReferenceEquals(t_current, this))
            throw new InvalidOperationException("Referenced storage captures must be disposed in stack order.");
        t_current = _parent;
        _disposed = true;
    }
}
