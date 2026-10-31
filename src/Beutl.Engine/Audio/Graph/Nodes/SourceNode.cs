using System;
using Beutl.Graphics.Rendering;
using Beutl.Media.Source;

namespace Beutl.Audio.Graph.Nodes;

public sealed class SourceNode : AudioNode
{
    public (SoundSource.Resource Resource, int Version)? Source { get; set; }

    internal override double? GetFiniteSourceEndSample(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (Source is not { } source || source.Resource.SampleRate <= 0)
            return 0;

        double seconds = source.Resource.MediaReader?.AudioInfo.Duration.ToDouble()
            ?? source.Resource.Duration.ToSeconds();
        return double.IsFinite(seconds) && seconds >= 0 ? seconds * sampleRate : null;
    }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Source == null)
            throw new InvalidOperationException("Source is not set.");

        var resource = Source.Value.Resource;
        var sampleCount = context.GetSampleCount();
        // Match the stereo output when Flush creates silence.
        RecordProcessedChannelCount(2);
        var buffer = new AudioBuffer(context.SampleRate, 2, sampleCount);

        // An unloaded or failed-to-open source has SampleRate == 0 and is unreadable — return silence
        // (this also avoids ToSamples throwing on the non-positive rate below).
        if (resource.SampleRate <= 0)
        {
            AudioReadScope.ReportFailure();
            return buffer;
        }

        long start = context.TimeRange.Start.ToSamples(resource.SampleRate, MidpointRounding.ToZero);
        long length = context.TimeRange.Duration.ToSamples(resource.SampleRate, MidpointRounding.ToPositiveInfinity);

        // Resource.Read takes int sample offsets, so an out-of-int-range start/length is unreadable —
        // return silence rather than wrapping the cast to a negative offset.
        if (start < 0 || length < 0 || start > int.MaxValue || length > int.MaxValue)
        {
            AudioReadScope.ReportFailure();
            return buffer;
        }

        try
        {
            if (resource.Read((int)start, (int)length, out AudioBuffer? audio))
            {
                using (audio)
                {
                    AudioChannelLayout layout = AudioChannelLayout.ForChannelCount(
                        resource.ChannelLayout, audio.ChannelCount);
                    var matrix = AudioChannelMatrix.Create(layout, AudioChannelLayout.Stereo);
                    if (audio.SampleCount == buffer.SampleCount && audio.SampleRate == buffer.SampleRate)
                    {
                        matrix.Apply(audio, buffer);
                    }
                    else
                    {
                        using var stereo = matrix.Apply(audio);
                        int copyLength = Math.Min(stereo.SampleCount, buffer.SampleCount);
                        stereo.GetChannelData(0)[..copyLength].CopyTo(buffer.GetChannelData(0));
                        stereo.GetChannelData(1)[..copyLength].CopyTo(buffer.GetChannelData(1));
                    }
                }
            }
            else
            {
                AudioReadScope.ReportFailure();
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
}
