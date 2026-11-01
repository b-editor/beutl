using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Editor.Models;
using Beutl.Extensions.FFmpeg;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    private readonly List<AudioFrameSnapshot> _audio = [];
    private Task _audioTask = Task.CompletedTask;

    internal void PublishAudio(AudioFrameSnapshot audio)
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested || audio.PlaybackId == 0) return;
            _audio.RemoveAll(a => a.PlaybackId != audio.PlaybackId);
            _audio.Add(audio);
            while (_audio.Count > 4) _audio.RemoveAt(0);
        }
    }

    internal void PublishStopped()
    {
        lock (_gate)
        {
            _audio.Clear();
            if (_latest is { } frame) _latest = frame with { Playing = false };
        }
    }

    private async Task EncodeAudioAsync()
    {
        PreviewEncoder? encoder = null;
        long nextSample = 0;
        // The encoder copies each packet into shared memory before it returns.
        byte[] packet = new byte[PreviewEncodingFormat.BufferBytes(0, 0, hdr: false, audio: true)];
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds((double)PreviewEncodingFormat.AudioPacketSamples / PreviewEncodingFormat.SampleRate));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                double clock = _mediaClock.Elapsed.TotalSeconds;
                bool sampled;
                long playbackId;
                lock (_gate)
                {
                    if (_peers.Count == 0 || _latest is not { } frame) { nextSample = 0; continue; }
                    playbackId = frame.PlaybackId;
                    sampled = SampleCurrentAudio(_audio, frame, clock, ref nextSample, MemoryMarshal.Cast<byte, float>(packet.AsSpan()));
                }
                if (!sampled) continue;
                encoder ??= new PreviewEncoder(0, 0, false, audio: true);
                byte[] data = await encoder.EncodeAsync(packet, false, _stop.Token).ConfigureAwait(false);
                lock (_gate)
                    if (_latest is not { Playing: true } current || current.PlaybackId != playbackId) continue;
                uint timestamp = (uint)nextSample;
                nextSample += PreviewEncodingFormat.AudioPacketSamples;
                if (data.Length != 0 && _transport is { } transport)
                    transport.SendMedia(data, timestamp, video: false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.CreateLogger<MobilePreviewServer>().LogError(ex, "Could not encode preview audio.");
            OnEncodingFailed();
        }
        finally { encoder?.Dispose(); }
    }

    // Writes one packet into output; false when there is nothing to play.
    internal static bool SampleCurrentAudio(IReadOnlyList<AudioFrameSnapshot> blocks, PendingFrame frame,
        double clock, ref long nextSample, Span<float> output)
    {
        const double rate = PreviewEncodingFormat.SampleRate;
        if (!frame.Playing) { nextSample = 0; return false; }
        // Audio advances between video frames, including at 1 fps. Pause clears the audio blocks
        // and Playing flag; seek/restart selects a different PlaybackId instead of expiring a frame.
        // RTP timestamps advance by one packet (960 samples), independent of timer jitter.
        if (nextSample == 0 || clock - nextSample / rate > 0.1) nextSample = (long)(clock * rate);
        return SampleAudio(blocks, frame.PlaybackId, frame.Time + nextSample / rate - frame.Clock, output);
    }

    internal static bool SampleAudio(IReadOnlyList<AudioFrameSnapshot> blocks, long playbackId, double time, Span<float> output)
    {
        const int channels = PreviewEncodingFormat.AudioChannels;
        const double rate = PreviewEncodingFormat.SampleRate;
        output.Clear();
        Dictionary<int, AudioChannelMatrix>? matrices = null;
        bool found = false;
        for (int i = 0; i < PreviewEncodingFormat.AudioPacketSamples; i++)
        {
            double position = time + i / rate;
            foreach (var block in blocks)
            {
                if (block.PlaybackId != playbackId || block.ChannelCount < 1 || block.SampleRate < 1) continue;
                double offset = (position - block.StartTime.ToTimeSpan().TotalSeconds) * block.SampleRate;
                int count = block.Interleaved.Length / block.ChannelCount;
                if (offset < 0 || offset >= count) continue;
                int first = (int)offset, second = Math.Min(first + 1, count - 1);
                float fraction = (float)(offset - first);
                for (int channel = 0; channel < channels; channel++)
                {
                    int c = Math.Min(channel, block.ChannelCount - 1);
                    float a = block.Interleaved[first * block.ChannelCount + c], b = block.Interleaved[second * block.ChannelCount + c];
                    if (block.ChannelCount > 2)
                    {
                        matrices ??= new();
                        if (!matrices.TryGetValue(block.ChannelCount, out AudioChannelMatrix? matrix))
                        {
                            matrix = AudioChannelMatrix.Create(AudioChannelLayout.GetDefault(block.ChannelCount), AudioChannelLayout.Stereo);
                            matrices.Add(block.ChannelCount, matrix);
                        }
                        a = b = 0;
                        for (int input = 0; input < block.ChannelCount; input++)
                        {
                            float gain = matrix.GetCoefficient(channel, input);
                            if (gain == 0) continue;
                            a += block.Interleaved[first * block.ChannelCount + input] * gain;
                            b += block.Interleaved[second * block.ChannelCount + input] * gain;
                        }
                    }
                    float sample = a + (b - a) * fraction;
                    output[i * channels + channel] = float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;
                }
                found = true;
                break;
            }
        }
        return found;
    }
}
