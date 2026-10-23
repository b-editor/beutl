using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

public sealed partial class ResampledInlineTailSound : Sound
{
    public const int SourceSampleRate = 48000;
    public const int SourceLatencySamples = 100;

    public ResampledInlineTailSound() => ScanProperties<ResampledInlineTailSound>();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var source = context.AddNode(new FixedLatencyNode(SourceLatencySamples));
        var clip = context.CreateClipNode(TimeRange.Value.Start, TimeRange.Value.Duration);
        context.Connect(source, clip);

        var resample = context.CreateResampleNode(SourceSampleRate);
        context.Connect(clip, resample);
        context.MarkAsOutput(resample);
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
