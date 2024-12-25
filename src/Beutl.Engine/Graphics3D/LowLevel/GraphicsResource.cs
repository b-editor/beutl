namespace Beutl.Graphics3D;

public class GraphicsResource : IDisposable
{
    protected GraphicsResource(Device device)
    {
        Device = device;
    }

    ~GraphicsResource()
    {
        if (IsDisposed) return;
        Dispose(false);
        IsDisposed = true;
    }

    public Device Device { get; }

    public bool IsDisposed { get; private set; }

    protected virtual void Dispose(bool disposing)
    {
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        Dispose(true);
        GC.SuppressFinalize(this);
        IsDisposed = true;
    }
}
