using Beutl.Graphics.Rendering;

namespace Beutl.Audio;

public class SoundNode : INode
{
    public SoundNode(Sound sound)
    {
        Sound = sound;
    }

    ~SoundNode()
    {
        if (IsDisposed)
        {
            OnDispose(false);
            IsDisposed = true;
        }
    }

    public Sound Sound { get; }

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
