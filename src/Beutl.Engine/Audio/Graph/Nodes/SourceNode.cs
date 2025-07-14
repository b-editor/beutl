using System;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

namespace Beutl.Audio.Graph.Nodes;

public sealed class SourceNode : AudioNode
{
    private ISoundSource? _source;

    public string SourceName { get; set; } = string.Empty;

    public ISoundSource? Source
    {
        get => _source;
        set
        {
            if (_source != value)
            {
                _source?.Dispose();
                _source = value;
            }
        }
    }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (_source == null)
            throw new InvalidOperationException("Source is not set.");

        var sampleCount = context.GetSampleCount();
        var buffer = new AudioBuffer(context.SampleRate, 2, sampleCount);

        // Read PCM data from source
        if (_source.Read(context.TimeRange.Start, context.TimeRange.Duration, out var pcm))
        {
            using (pcm)
            {
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _source?.Dispose();
            _source = null;
        }

        base.Dispose(disposing);
    }
}
