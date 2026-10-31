using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Editor.Models;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Silk.NET.OpenAL;
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
        // Admit idle composition on the UI thread, like history mutations. A background visualizer
        // continuation must not enqueue fresh scene work between Pause and the synchronous mutation.
        return await await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            lock (_renderRequestLock)
            {
                if (_isDisposing || _isPausing)
                    return Task.FromResult<AudioFrameSnapshot?>(null);

                SceneComposer? composer = EditViewModel.Composer.Value;
                if (composer == null || composer.IsDisposed)
                    return Task.FromResult<AudioFrameSnapshot?>(null);

                return ComposeThread.Dispatcher.InvokeAsync(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    // The UI thread can dispose this composer (rebuild-by-replacement on frame-size changes)
                    // after the calling-thread check above, so re-check on the compose thread and report
                    // "no audio" instead of racing a disposed compositor.
                    if (_isDisposing || _isPausing || composer.IsDisposed)
                        return (AudioFrameSnapshot?)null;

                    AudioBuffer? audio;
                    try
                    {
                        audio = composer.Compose(new TimeRange(start, duration));
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
            if (OperatingSystem.IsWindows())
            {
                using var audioContext = new XAudioContext();
                await PlayWithXA2(audioContext, scene, clock, startTime, sampleRate, generation, playbackToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Run(async () =>
                {
                    playbackToken.ThrowIfCancellationRequested();
                    using var audioContext = new AudioContext();
                    await PlayWithOpenAL(audioContext, scene, clock, startTime, sampleRate, generation, playbackToken);
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
                NotificationService.ShowError(MessageStrings.UnexpectedError,
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
            AudioBuffer? audio = composer.Compose(new TimeRange(time, s_second));
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

    // オーディオバックエンドが最初のサンプルを出力するまで短いポーリングで待機する。出力前にアンカーすると
    // 映像が先行し、後の AnchorClock で巻き戻る。true を返した場合、呼び出し側は安全に AnchorClock できる。
    // 期限内に進行が観測できなかった場合と音声がない場合は false を返し、呼び出し側は音声クロックを諦めて
    // 壁時計にフォールバックする (サスペンド/切断中のデバイスで無期限ハングするのを防ぐため)。このとき
    // AnchorClock は何もしないので、呼び出し側が SignalStarted で開始を知らせる。
    private static async Task<bool> WaitForFirstSampleAsync(
        Func<bool> hasProgressed, bool hasAudio, CancellationToken token)
    {
        if (!hasAudio) return false;
        long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2; // 2s
        while (!token.IsCancellationRequested)
        {
            if (hasProgressed()) return true;
            if (Stopwatch.GetTimestamp() >= deadline) return false;
            try
            {
                await Task.Delay(1, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        return false;
    }

    private async Task PlayWithXA2(XAudioContext audioContext, Scene scene,
        AudioPlaybackClock clock, MediaTime startTime, int sampleRate, int generation, CancellationToken playbackToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(playbackToken);
        cts.Token.ThrowIfCancellationRequested();
        MediaTime cur = startTime;
        MediaTime sceneEndTime = default;
        var source = new XAudioSource(audioContext);
        var primaryBuffer = new XAudioBuffer();
        var secondaryBuffer = new XAudioBuffer();
        bool hasAudio = false;
        bool audioClockValid = false;

        void PrepareBuffer(XAudioBuffer buffer)
        {
            cts.Token.ThrowIfCancellationRequested();
            MediaTime bufferStartTime = cur;
            var bufferData = FillAudioData(cur, scene, generation, playbackToken);
            sceneEndTime = bufferData.SceneEnd;
            using AudioBuffer? audio = bufferData.Audio;
            cts.Token.ThrowIfCancellationRequested();
            if (audio != null)
            {
                if (!hasAudio)
                {
                    sampleRate = audio.SampleRate;
                }

                using AudioBuffer? stereo = audio.ChannelCount == 2 ? null
                    : AudioChannelMatrix.Create(EditViewModel.Composer.Value.ChannelLayout, AudioChannelLayout.Stereo).Apply(audio);
                buffer.BufferData(stereo ?? audio);
                hasAudio = true;
                _sessionGuard.TryApply(generation, () => PublishAudioSnapshot(audio, bufferStartTime));
            }

            source.QueueBuffer(buffer);
        }

        void AnchorClock()
        {
            if (!hasAudio || !audioClockValid) return;
            double seconds = (double)source.SamplesPlayed / sampleRate;
            clock.Anchor(startTime + MediaTime.FromSeconds(seconds));
        }

        async Task PlaybackLoop()
        {
            PrepareBuffer(primaryBuffer);

            cur += s_second;
            PrepareBuffer(secondaryBuffer);

            source.Play();
            // 最初のサンプルが出てからアンカーする (WaitForFirstSampleAsync を参照)。
            audioClockValid = await WaitForFirstSampleAsync(
                    () => source.SamplesPlayed > 0, hasAudio, cts.Token)
                .ConfigureAwait(false);
            if (hasAudio && !audioClockValid)
            {
                _logger.LogWarning(
                    "XAudio2 backend did not advance SamplesPlayed within the startup deadline; falling back to wall-clock timing.");
            }
            AnchorClock();
            clock.SignalStarted();

            await Task.Delay(1000, cts.Token).ConfigureAwait(false);
            AnchorClock();

            // primaryBufferが終了、secondaryが開始

            // FillAudioData refreshes the range on the compose thread along with each buffer.
            while (cur < sceneEndTime)
            {
                if (!IsPlaying.Value)
                {
                    source.Stop();
                    break;
                }

                cur += s_second;

                PrepareBuffer(primaryBuffer);

                (primaryBuffer, secondaryBuffer) = (secondaryBuffer, primaryBuffer);

                await Task.Delay(1000, cts.Token).ConfigureAwait(false);
                AnchorClock();
            }
        }

        try
        {
            await RunAudioPlaybackWithImmediateStopAsync(cts.Token, source.Stop, PlaybackLoop)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            source.Stop();
        }
        finally
        {
            source.Dispose();
            primaryBuffer.Dispose();
            secondaryBuffer.Dispose();
        }
    }

    internal static async Task RunAudioPlaybackWithImmediateStopAsync(
        CancellationToken token, Action stop, Func<Task> playback)
    {
        using CancellationTokenRegistration stopPlayback = token.Register(stop);
        await playback().ConfigureAwait(false);
    }

    private async Task PlayWithOpenAL(AudioContext audioContext, Scene scene,
        AudioPlaybackClock clock, MediaTime startTime, int sampleRate, int generation, CancellationToken playbackToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(playbackToken);
        cts.Token.ThrowIfCancellationRequested();
        audioContext.MakeCurrent();

        MediaTime cur = startTime;
        MediaTime sceneEndTime = default;
        uint[] buffers = audioContext.GenBuffers(2);
        uint source = audioContext.GenSource();
        long totalProcessedSamples = 0;
        var queuedBufferSamples = new Queue<int>();
        bool hasAudio = false;
        bool audioClockValid = false;

        void AnchorClock()
        {
            if (!hasAudio || !audioClockValid) return;
            audioContext.GetSource(source, GetSourceInteger.SampleOffset, out int sampleOffset);
            long pos = totalProcessedSamples + sampleOffset;
            double seconds = (double)pos / sampleRate;
            clock.Anchor(startTime + MediaTime.FromSeconds(seconds));
        }

        // Composes the next second and queues it in the buffer takeBuffer returns once the audio is ready.
        void FillAndQueue(Func<uint> takeBuffer)
        {
            cts.Token.ThrowIfCancellationRequested();
            MediaTime bufferStartTime = cur;
            var bufferData = FillAudioData(cur, scene, generation, playbackToken);
            sceneEndTime = bufferData.SceneEnd;
            using AudioBuffer? audio = bufferData.Audio;
            cts.Token.ThrowIfCancellationRequested();
            cur += s_second;
            uint buffer = takeBuffer();
            int fillSamples = 0;
            if (audio != null)
            {
                if (!hasAudio)
                {
                    sampleRate = audio.SampleRate;
                }

                using AudioBuffer? stereo = audio.ChannelCount == 2 ? null
                    : AudioChannelMatrix.Create(EditViewModel.Composer.Value.ChannelLayout, AudioChannelLayout.Stereo).Apply(audio);
                audioContext.BufferData(buffer, stereo ?? audio);
                fillSamples = audio.SampleCount;
                hasAudio = true;
                _sessionGuard.TryApply(generation, () => PublishAudioSnapshot(audio, bufferStartTime));
            }

            audioContext.SourceQueueBuffer(source, buffer);
            queuedBufferSamples.Enqueue(fillSamples);
        }

        try
        {
            foreach (uint buffer in buffers)
            {
                FillAndQueue(() => buffer);
            }

            cts.Token.ThrowIfCancellationRequested();
            audioContext.SourcePlay(source);

            try
            {
                // 最初のサンプルが出てからアンカーする (WaitForFirstSampleAsync を参照)。
                audioClockValid = await WaitForFirstSampleAsync(
                        () =>
                        {
                            audioContext.MakeCurrent();
                            audioContext.GetSource(source, GetSourceInteger.SampleOffset, out int offset);
                            return offset > 0;
                        },
                        hasAudio,
                        cts.Token)
                    .ConfigureAwait(false);
                // await 後は別のプールスレッドで継続する可能性があり、
                // OpenAL コンテキストはスレッド固有のため再バインドが必要。
                audioContext.MakeCurrent();
                if (hasAudio && !audioClockValid)
                {
                    _logger.LogWarning(
                        "OpenAL backend did not advance SampleOffset within the startup deadline; falling back to wall-clock timing.");
                }
                AnchorClock();
                clock.SignalStarted();

                while (IsPlaying.Value && !cts.Token.IsCancellationRequested)
                {
                    audioContext.MakeCurrent();
                    audioContext.GetSource(source, GetSourceInteger.BuffersProcessed, out int processed);
                    while (processed > 0)
                    {
                        FillAndQueue(() => audioContext.SourceUnqueueBuffer(source));
                        // The oldest entry is the reused buffer's; FillAndQueue added the new one behind it.
                        totalProcessedSamples += queuedBufferSamples.Dequeue();
                        processed--;
                    }

                    if (audioContext.GetSourceState(source) != SourceState.Playing)
                    {
                        audioContext.SourcePlay(source);
                    }

                    AnchorClock();

                    await Task.Delay(100, cts.Token).ConfigureAwait(false);
                    if (cur >= sceneEndTime)
                        break;
                }

                while (audioContext.GetSourceState(source) == SourceState.Playing
                       && IsPlaying.Value
                       && !cts.Token.IsCancellationRequested)
                {
                    audioContext.MakeCurrent();
                    audioContext.GetSource(source, GetSourceInteger.BuffersProcessed, out int drainProcessed);
                    while (drainProcessed > 0 && queuedBufferSamples.Count > 0)
                    {
                        audioContext.SourceUnqueueBuffer(source);
                        totalProcessedSamples += queuedBufferSamples.Dequeue();
                        drainProcessed--;
                    }
                    AnchorClock();
                    await Task.Delay(100, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            audioContext.MakeCurrent();
            audioContext.SourceStop(source);
            // https://hamken100.blogspot.com/2014/04/aldeletebuffersalinvalidoperation.html
            audioContext.Source(source, SourceInteger.Buffer, 0);
            audioContext.DeleteBuffers(buffers);
            audioContext.DeleteSource(source);
        }
    }
}
