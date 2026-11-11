namespace Beutl.Audio.Graph;

// A node that reads its input at SourceSampleRate whatever rate it is asked for, so tail accounting changes rates
// where it passes one.
internal interface IAudioSampleRateConversionNode
{
    int SourceSampleRate { get; }
}
