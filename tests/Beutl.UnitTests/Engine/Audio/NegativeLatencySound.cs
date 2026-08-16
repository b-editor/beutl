using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

public sealed partial class NegativeLatencySound : Sound
{
    public NegativeLatencySound() => ScanProperties<NegativeLatencySound>();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var output = context.AddNode(new NegativeLatencyNode());
        context.MarkAsOutput(output);
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }

    private sealed class NegativeLatencyNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override int GetTotalLatencySamples(int sampleRate) => -1;
    }
}
