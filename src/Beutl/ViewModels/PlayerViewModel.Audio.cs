using System.Runtime.Versioning;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Platforms.AVFoundation;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Editor.Models;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using AudioContext = Beutl.Audio.Platforms.OpenAL.AudioContext;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    internal void PublishAudioSnapshot(AudioBuffer? audio, MediaTime startTime)
    {
        // An abandoned audio task may still publish after disposal begins.
        if (audio == null || _isDisposing) return;

        // Always publish so the ReplaySubject retains the latest snapshot — a
        // visualizer tab opened after this point can replay it on subscribe.
        var interleaved = new float[audio.SampleCount * audio.ChannelCount];
        AudioConversion.Interleave(audio, interleaved);
        // Teardown completes the subject so a racing publish is ignored.
        _audioFramePushed.OnNext(new AudioFrameSnapshot(interleaved, audio.SampleRate, audio.ChannelCount, startTime)
        {
            PlaybackId = Interlocked.Read(ref _previewPlaybackId)
        });
    }

    async Task<AudioFrameSnapshot?> IPreviewPlayer.ComposeAudioAsync(MediaTime start, MediaTime duration, CancellationToken ct)
    {
        // Admit idle composition alongside history mutations on the UI thread. Device playback
        // owns the shared composer for the entire playback task, including loop restarts.
        return await await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            lock (_renderRequestLock)
            {
                if (_isDisposing || IsPlaybackActive)
                    return Task.FromResult<AudioFrameSnapshot?>(null);

                SceneComposer? composer = EditViewModel.Composer.Value;
                if (composer == null || composer.IsDisposed)
                    return Task.FromResult<AudioFrameSnapshot?>(null);

                return ComposeThread.Dispatcher.InvokeAsync(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    // Playback may start or the UI may replace the composer while this read is queued.
                    if (_isDisposing || IsPlaybackActive || composer.IsDisposed)
                        return (AudioFrameSnapshot?)null;

                    AudioBuffer? audio;
                    try
                    {
                        Scene scene = composer.Compositor.Scene;
                        audio = composer.ComposePlayback(new TimeRange(start, duration), scene.Start.Value + scene.Duration.Value);
                    }
                    catch (ObjectDisposedException)
                    {
                        // TOCTOU: the UI thread can dispose the composer between the IsDisposed re-check above
                        // and this call (lockless rebuild-by-replacement on a frame-size change). Degrade to
                        // "no audio" rather than surfacing the race as a throw on a throwaway background compose.
                        return (AudioFrameSnapshot?)null;
                    }

                    using (audio)
                    {
                        if (audio == null) return (AudioFrameSnapshot?)null;

                        int samples = audio.SampleCount;
                        int channels = audio.ChannelCount;
                        var interleaved = new float[samples * channels];
                        for (int c = 0; c < channels; c++)
                        {
                            Span<float> src = audio.GetChannelData(c);
                            for (int f = 0; f < samples; f++)
                            {
                                interleaved[f * channels + c] = src[f];
                            }
                        }
                        return (AudioFrameSnapshot?)new AudioFrameSnapshot(interleaved, audio.SampleRate, channels, start);
                    }
                }, ct: ct);
            }
        }, Avalonia.Threading.DispatcherPriority.Normal, ct);
    }

    private async Task PlayAudio(
        Scene scene,
        AudioPlaybackClock clock,
        MediaTime startTime,
        int sampleRate,
        int generation,
        CancellationToken playbackToken)
    {
        try
        {
            playbackToken.ThrowIfCancellationRequested();
            AudioChannelLayout projectLayout = EditViewModel.Composer.Value.ChannelLayout;
            bool useMultichannelBackend = AudioPlaybackOutput.UseMultichannelBackend(projectLayout);
            if (OperatingSystem.IsWindows())
            {
                using var audioContext = new XAudioContext(useDeviceDefaults: useMultichannelBackend);
                await PlayWithXA2(audioContext, projectLayout, useMultichannelBackend,
                    scene, clock, startTime, sampleRate, generation, playbackToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Run(async () =>
                {
                    playbackToken.ThrowIfCancellationRequested();
                    if (OperatingSystem.IsMacOS() && useMultichannelBackend)
                    {
                        using var renderer = AudioPlaybackOutput.TryCreateAVSampleBufferRenderer(
                            projectLayout, isMacOS: true,
                            () => OperatingSystem.IsMacOS()
                                ? new AVSampleBufferAudioRenderer(projectLayout, sampleRate)
                                : throw new PlatformNotSupportedException(), _logger);
                        if (renderer != null)
                        {
                            try
                            {
                                await PlayWithAVSampleBufferRenderer(renderer, scene, clock, startTime,
                                    sampleRate, generation, playbackToken).ConfigureAwait(false);
                                return;
                            }
                            catch (AVSampleBufferAudioRendererException ex) when (!clock.StartedTask.IsCompleted)
                            {
                                // Only failures before startup is signaled fall back; later failures use PlayAudio's error handler.
                                _logger.LogWarning(ex, "AVSampleBufferAudioRenderer failed; falling back to OpenAL Soft.");
                            }
                        }

                        playbackToken.ThrowIfCancellationRequested();
                        if (!IsPlaying.Value || !_sessionGuard.Owns(generation)) return;
                    }

                    using var audioContext = new AudioContext(soft: useMultichannelBackend);
                    await PlayWithOpenAL(audioContext, projectLayout,
                        scene, clock, startTime, sampleRate, generation, playbackToken);
                }, playbackToken);
            }
        }
        catch (OperationCanceledException) when (playbackToken.IsCancellationRequested || !_sessionGuard.Owns(generation))
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred during audio playback.");
            // On the UI thread, like the other playback state changes.
            await TryApplyOnUIThreadAsync(generation, () =>
            {
                EditViewModel.GetRequiredService<INotificationService>().ShowError(MessageStrings.UnexpectedError,
                    MessageStrings.AudioPlaybackException);
                IsPlaying.Value = false;
            });
        }
        finally
        {
            clock.Pause();
        }
    }

    private async Task CancelPlaybackAsync(CancellationTokenSource source)
    {
        try
        {
            // CancelAsync sets the token immediately, but runs native Stop callbacks off the UI
            // thread. The backend deadline must also cover a Stop callback that never returns.
            await source.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while cancelling audio playback.");
        }
    }

    internal (AudioBuffer? Audio, MediaTime SceneEnd) FillAudioData(
        MediaTime time, Scene scene, int generation, CancellationToken playbackToken)
    {
        return ComposeThread.Dispatcher.Invoke(() =>
        {
            // This check must be inside the dispatch: a buffer queued before Pause can execute
            // after its drain barrier. A stopping or retired session must not read scene state,
            // including the interval before a queued PauseCore has retired the generation.
            if (_isPausing || playbackToken.IsCancellationRequested || !_sessionGuard.Owns(generation))
                return ((AudioBuffer?)null, MediaTime.Zero);

            MediaTime sceneEnd = scene.Start.Value + scene.Duration.Value;
            SceneComposer composer = EditViewModel.Composer.Value;
            AudioBuffer? audio = composer.ComposePlayback(new TimeRange(time, s_second), sceneEnd, generation);
            if (audio is not null)
                SilenceTailBeyondSceneEnd(audio, time, sceneEnd);

            return (audio, sceneEnd);
        });
    }

    // Scene.Start + Duration を超えた末尾サンプルをゼロ埋めする。
    // 編集中に Duration が縮んでバッファ全体が範囲外になるケースもありうる。
    internal static void SilenceTailBeyondSceneEnd(
        AudioBuffer audio, MediaTime bufferStart, MediaTime sceneEndTime)
    {
        if (sceneEndTime <= bufferStart)
        {
            audio.Clear();
            return;
        }

        MediaTime bufferEnd = bufferStart + MediaTime.FromSeconds(audio.SampleCount / (double)audio.SampleRate);
        if (bufferEnd <= sceneEndTime) return;

        double keepSeconds = (sceneEndTime - bufferStart).ToSeconds();
        int keepSamples = (int)Math.Ceiling(keepSeconds * audio.SampleRate);
        keepSamples = Math.Clamp(keepSamples, 0, audio.SampleCount);

        for (int channel = 0; channel < audio.ChannelCount; channel++)
        {
            audio.GetChannelData(channel)[keepSamples..].Clear();
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task PlayWithXA2(XAudioContext audioContext, AudioChannelLayout projectLayout,
        bool useMultichannelBackend, Scene scene,
        AudioPlaybackClock clock, MediaTime startTime, int sampleRate, int generation, CancellationToken playbackToken)
    {
        playbackToken.ThrowIfCancellationRequested();
        using var sink = new XAudio2AudioSink(audioContext, projectLayout, useMultichannelBackend);
        AudioSinkRefillLoop loop = CreateRefillLoop(sink, scene, clock, startTime, sampleRate, generation,
            playbackToken,
            () => _logger.LogWarning(
                "XAudio2 backend did not advance SamplesPlayed within the startup deadline; falling back to wall-clock timing."));
        // XAudio2 can stop the voice from any thread, so cancellation silences it at once
        // instead of waiting for a synchronous buffer composition to return.
        await RunAudioPlaybackWithImmediateStopAsync(playbackToken, sink.Stop, () => loop.RunAsync(playbackToken))
            .ConfigureAwait(false);
    }

    private AudioSinkRefillLoop CreateRefillLoop(IAudioSink sink, Scene scene, AudioPlaybackClock clock,
        MediaTime startTime, int sampleRate, int generation, CancellationToken playbackToken, Action onStartupStalled)
    {
        return new AudioSinkRefillLoop
        {
            Sink = sink,
            Clock = clock,
            StartTime = startTime,
            SampleRate = sampleRate,
            Compose = time => FillAudioData(time, scene, generation, playbackToken),
            Publish = (audio, bufferStartTime) =>
                _sessionGuard.TryApply(generation, () => PublishAudioSnapshot(audio, bufferStartTime)),
            IsPlaying = () => IsPlaying.Value,
            OnStartupStalled = onStartupStalled,
        };
    }

    internal static async Task RunAudioPlaybackWithImmediateStopAsync(
        CancellationToken token, Action stop, Func<Task> playback)
    {
        using CancellationTokenRegistration stopPlayback = token.Register(stop);
        await playback().ConfigureAwait(false);
    }

    [SupportedOSPlatform("macos")]
    private async Task PlayWithAVSampleBufferRenderer(AVSampleBufferAudioRenderer renderer, Scene scene,
        AudioPlaybackClock clock, MediaTime startTime, int sampleRate, int generation, CancellationToken playbackToken)
    {
        MediaTime cur = startTime;
        MediaTime sceneEndTime = default;
        long nextSample = 0;
        bool hasAudio = false;
        bool audioClockValid = false;
        bool started = false;

        bool IsActive() => IsPlaying.Value && !playbackToken.IsCancellationRequested && _sessionGuard.Owns(generation);

        void CheckPlayback()
        {
            if (!IsActive()) throw new OperationCanceledException(playbackToken);
        }

        void AnchorClock()
        {
            if (hasAudio && audioClockValid)
                clock.Anchor(startTime + MediaTime.FromSeconds(renderer.PositionSeconds));
        }

        async Task FillAndQueue()
        {
            CheckPlayback();
            MediaTime bufferStartTime = cur;
            var bufferData = FillAudioData(cur, scene, generation, playbackToken);
            sceneEndTime = bufferData.SceneEnd;
            using AudioBuffer? audio = bufferData.Audio;
            CheckPlayback();

            if (audio is { SampleCount: > 0 })
            {
                long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
                // Composition runs outside the renderer's gate, so cancellation can stop native
                // playback immediately even if the compose dispatcher is blocked.
                while (!renderer.TryEnqueue(audio, nextSample))
                {
                    CheckPlayback();
                    if (!started && Stopwatch.GetTimestamp() >= deadline)
                        throw new AVSampleBufferAudioRendererException("The renderer was not ready within the startup deadline.");
                    await Task.Delay(10, playbackToken).ConfigureAwait(false);
                }

                nextSample += audio.SampleCount;
                hasAudio = true;
                _sessionGuard.TryApply(generation, () => PublishAudioSnapshot(audio, bufferStartTime));
            }
            else
            {
                nextSample += sampleRate;
            }
            cur += s_second;
        }

        async Task PlaybackLoop()
        {
            await FillAndQueue().ConfigureAwait(false);
            // Do not wait for a second buffer at rate 0 if the renderer is already backpressuring.
            if (cur < sceneEndTime && renderer.IsReadyForMoreMediaData)
                await FillAndQueue().ConfigureAwait(false);

            CheckPlayback();
            renderer.Play();
            started = true;
            audioClockValid = await AudioSinkRefillLoop.WaitForFirstSampleAsync(
                () => renderer.PositionSeconds > 0, hasAudio, playbackToken).ConfigureAwait(false);
            CheckPlayback();
            if (hasAudio && !audioClockValid)
                throw new AVSampleBufferAudioRendererException("The timebase did not advance within the startup deadline.");
            AnchorClock();
            clock.SignalStarted();

            while (IsActive() && cur < sceneEndTime)
            {
                // FillAudioData composes one second. Only append when it fits in a two-second
                // window ahead of the native playhead, and keep the project's channels unchanged.
                if (nextSample - renderer.PositionSeconds * sampleRate <= sampleRate
                    && renderer.IsReadyForMoreMediaData)
                {
                    await FillAndQueue().ConfigureAwait(false);
                }
                AnchorClock();
                await Task.Delay(100, playbackToken).ConfigureAwait(false);
            }

            // The final chunk is silence-padded at the scene boundary, just like OpenAL's buffers.
            while (IsActive() && hasAudio && renderer.PositionSeconds < (double)nextSample / sampleRate)
            {
                AnchorClock();
                await Task.Delay(100, playbackToken).ConfigureAwait(false);
            }
        }

        try
        {
            await RunAudioPlaybackWithImmediateStopAsync(playbackToken, renderer.Stop, PlaybackLoop)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            renderer.Stop();
        }
    }

    private async Task PlayWithOpenAL(AudioContext audioContext, AudioChannelLayout projectLayout, Scene scene,
        AudioPlaybackClock clock, MediaTime startTime, int sampleRate, int generation, CancellationToken playbackToken)
    {
        playbackToken.ThrowIfCancellationRequested();
        using var sink = new OpenALAudioSink(audioContext, projectLayout);
        AudioSinkRefillLoop loop = CreateRefillLoop(sink, scene, clock, startTime, sampleRate, generation,
            playbackToken,
            () => _logger.LogWarning(
                "OpenAL backend did not advance SampleOffset within the startup deadline; falling back to wall-clock timing."));
        await loop.RunAsync(playbackToken).ConfigureAwait(false);
    }
}
