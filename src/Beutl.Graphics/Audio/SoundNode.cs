using Beutl.Graphics.Rendering;

namespace Beutl.Audio;

public sealed class SoundNode : INode
{
    public SoundNode(Sound sound) => Sound = sound;

    public Sound Sound { get; }

    public void Dispose()
    {
    }
}
