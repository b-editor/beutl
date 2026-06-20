using System;
using Beutl.Graphics.Rendering;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

namespace Beutl.Audio.Graph.Nodes;

public sealed class SourceNode : AudioNode
{
    public (SoundSource.Resource Resource, int Version)? Source { get; set; }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Source == null)
            throw new InvalidOperationException("Source is not set.");

        var resource = Source.Value.Resource;
        var sampleCount = context.GetSampleCount();
        var buffer = new AudioBuffer(context.SampleRate, 2, sampleCount);

        // An unloaded or failed-to-open source keeps SampleRate == 0 (its MediaReader never opened — e.g.
        // a moved / deleted / unsupported file). Such a source is unreadable, so return silence: feeding 0
        // into TimeToSampleIndex below would throw (non-positive rate) and leak this buffer before the try
        // block, regressing the prior "missing audio file -> silence" behavior into a render-pipeline throw.
        if (resource.SampleRate <= 0)
        {
            return buffer;
        }

        long start = AudioMath.TimeToSampleIndex(context.TimeRange.Start, resource.SampleRate);
        long length = (long)Math.Ceiling(context.TimeRange.Duration.TotalSeconds * resource.SampleRate);

        // Resource.Read (and downstream decoders) take int sample offsets, so a source whose start
        // exceeds int.MaxValue samples is unreadable regardless. Treat an out-of-int-range offset
        // as silence instead of letting the unchecked (int) cast wrap to int.MinValue (wrong seek /
        // audio on long timelines at high sample rates).
        if (start < 0 || length < 0 || start > int.MaxValue || length > int.MaxValue)
        {
            return buffer;
        }

        try
        {
            // Read PCM data from source
            if (resource.Read((int)start, (int)length, out var pcmRef))
            {
                using (pcmRef)
                {
                    var pcm = pcmRef.Value;
                    // Convert to stereo float if needed
                    if (pcm is Pcm<Stereo32BitFloat> stereoPcm)
                    {
                        CopyPcmToBuffer(stereoPcm, buffer);
                    }
                    else if (pcm is Pcm<Monaural32BitFloat> monoPcm)
                    {
                        CopyMonoToStereoBuffer(monoPcm, buffer);
                    }
                    else
                    {
                        // Convert to stereo float
                        using var convertedPcm = pcm.Convert<Stereo32BitFloat>();
                        CopyPcmToBuffer(convertedPcm, buffer);
                    }
                }
            }

            return buffer;
        }
        catch
        {
            // Dispose the buffer the caller never received rather than leak it.
            buffer.Dispose();
            throw;
        }
    }

    private static unsafe void CopyPcmToBuffer(Pcm<Stereo32BitFloat> pcm, AudioBuffer buffer)
    {
        var srcPtr = (Stereo32BitFloat*)pcm.Data;
        var leftChannel = buffer.GetChannelData(0);
        var rightChannel = buffer.GetChannelData(1);

        var copyLength = System.Math.Min(pcm.NumSamples, buffer.SampleCount);

        for (int i = 0; i < copyLength; i++)
        {
            leftChannel[i] = srcPtr[i].Left;
            rightChannel[i] = srcPtr[i].Right;
        }
    }

    private static unsafe void CopyMonoToStereoBuffer(Pcm<Monaural32BitFloat> pcm, AudioBuffer buffer)
    {
        var srcPtr = (Monaural32BitFloat*)pcm.Data;
        var leftChannel = buffer.GetChannelData(0);

        var copyLength = System.Math.Min(pcm.NumSamples, buffer.SampleCount);

        // Copy mono to left channel
        for (int i = 0; i < copyLength; i++)
        {
            leftChannel[i] = srcPtr[i].Value;
        }

        // Copy to right channel if stereo
        if (buffer.ChannelCount > 1)
        {
            var rightChannel = buffer.GetChannelData(1);
            leftChannel.CopyTo(rightChannel);
        }
    }
}
