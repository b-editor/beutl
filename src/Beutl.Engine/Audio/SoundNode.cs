using Beutl.Graphics.Rendering;

namespace Beutl.Audio;

public class SoundNode(Sound sound) : INode
{
    ~SoundNode()
    {
        if (IsDisposed)
        {
            OnDispose(false);
            IsDisposed = true;
        }
    }

    public Sound Sound { get; } = sound;

    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed)
        {
            OnDispose(true);
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void OnDispose(bool disposing)
    {
    }

}
