using System.Runtime.InteropServices;
using Beutl.Editor.Models;
using Beutl.Extensions.FFmpeg;
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
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                double clock = _mediaClock.Elapsed.TotalSeconds;
                float[]? samples;
                long playbackId;
                lock (_gate)
                {
                    if (_peers.Count == 0 || _latest is not { Playing: true } frame) { nextSample = 0; continue; }
                    // Playback's audio is read ahead in one-second blocks. Select only the 20 ms
                    // being displayed now, not the entire block, so pause/seek cannot queue seconds of audio.
                    if (clock - frame.Clock > 0.25) continue;
                    playbackId = frame.PlaybackId;
                    // RTP audio timestamps advance by exactly one packet (960 samples). Wall-clock
                    // jitter must not create overlaps/gaps that make Opus packets unusable at the receiver.
                    if (nextSample == 0 || clock - nextSample / 48000d > 0.1) nextSample = (long)(clock * 48000);
                    samples = SampleAudio(_audio, frame.PlaybackId, frame.Time + nextSample / 48000d - frame.Clock);
                }
                if (samples is null) continue;
                encoder ??= new PreviewEncoder(0, 0, false, audio: true);
                byte[] data = await encoder.EncodeAsync(MemoryMarshal.AsBytes(samples.AsSpan()).ToArray(), false, _stop.Token).ConfigureAwait(false);
                lock (_gate)
                    if (_latest is not { Playing: true } current || current.PlaybackId != playbackId) continue;
                uint timestamp = (uint)nextSample;
                nextSample += 960;
                if (data.Length != 0 && _transport is { } transport)
                    await transport.SendAsync(new { type = "audio", data, timestamp }, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.CreateLogger<MobilePreviewServer>().LogError(ex, "Could not encode preview audio.");
            OnTransportStopped();
        }
        finally { encoder?.Dispose(); }
    }

    internal static float[]? SampleAudio(IReadOnlyList<AudioFrameSnapshot> blocks, long playbackId, double time)
    {
        var output = new float[960 * 2];
        bool found = false;
        for (int i = 0; i < 960; i++)
        {
            double position = time + i / 48000d;
            foreach (var block in blocks)
            {
                if (block.PlaybackId != playbackId || block.ChannelCount < 1 || block.SampleRate < 1) continue;
                double offset = (position - block.StartTime.ToTimeSpan().TotalSeconds) * block.SampleRate;
                int count = block.Interleaved.Length / block.ChannelCount;
                if (offset < 0 || offset >= count) continue;
                int first = (int)offset, second = Math.Min(first + 1, count - 1);
                float fraction = (float)(offset - first);
                for (int channel = 0; channel < 2; channel++)
                {
                    int c = Math.Min(channel, block.ChannelCount - 1);
                    float a = block.Interleaved[first * block.ChannelCount + c], b = block.Interleaved[second * block.ChannelCount + c];
                    float sample = a + (b - a) * fraction;
                    output[i * 2 + channel] = float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;
                }
                found = true;
                break;
            }
        }
        return found ? output : null;
    }
}
