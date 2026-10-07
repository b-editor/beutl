using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Editor.Models;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Silk.NET.OpenAL;
using Vortice.Multimedia;
using AudioContext = Beutl.Audio.Platforms.OpenAL.AudioContext;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    internal void PublishAudioSnapshot(Pcm<Stereo32BitFloat>? pcm, TimeSpan startTime)
    {
        // An abandoned audio task may still publish after disposal begins.
        if (pcm == null || _isDisposing) return;

        // Always publish so the ReplaySubject retains the latest snapshot — a
        // visualizer tab opened after this point can replay it on subscribe.
        int samples = pcm.NumSamples;
        int channels = pcm.NumChannels;
        var interleaved = new float[samples * channels];
        MemoryMarshal.Cast<Stereo32BitFloat, float>(pcm.DataSpan).CopyTo(interleaved);
        // Teardown completes the subject so a racing publish is ignored.
        _audioFramePushed.OnNext(new AudioFrameSnapshot(interleaved, pcm.SampleRate, channels, startTime));
    }

    async Task<AudioFrameSnapshot?> IPreviewPlayer.ComposeAudioAsync(TimeSpan start, TimeSpan duration, CancellationToken ct)
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
        TimeSpan startTime,
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
            _sessionGuard.TryApply(generation, () =>
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

    internal (Pcm<Stereo32BitFloat>? Pcm, TimeSpan SceneEnd) FillAudioData(
        TimeSpan time, Scene scene, int generation, CancellationToken playbackToken)
    {
        return ComposeThread.Dispatcher.Invoke(() =>
        {
            // This check must be inside the dispatch: a buffer queued before Pause can execute
            // after its drain barrier. A stopping or retired session must not read scene state,
            // including the interval before a queued PauseCore has retired the generation.
            if (_isPausing || playbackToken.IsCancellationRequested || !_sessionGuard.Owns(generation))
                return ((Pcm<Stereo32BitFloat>?)null, TimeSpan.Zero);

            TimeSpan sceneEnd = scene.Start + scene.Duration;
            SceneComposer composer = EditViewModel.Composer.Value;
            using var audio = composer.Compose(new TimeRange(time, s_second));
            if (audio is null)
                return ((Pcm<Stereo32BitFloat>?)null, sceneEnd);

            var pcm = audio.ToPcm();
            SilenceTailBeyondSceneEnd(pcm, time, sceneEnd);
            return (pcm, sceneEnd);
        });
    }

    // Scene.Start + Duration を超えた末尾サンプルをゼロ埋めする。
    // 編集中に Duration が縮んでバッファ全体が範囲外になるケースもありうる。
    private static void SilenceTailBeyondSceneEnd(
        Pcm<Stereo32BitFloat> pcm, TimeSpan bufferStart, TimeSpan sceneEndTime)
    {
        if (sceneEndTime <= bufferStart)
        {
            pcm.DataSpan.Clear();
            return;
        }

        TimeSpan bufferEnd = bufferStart + pcm.Duration;
        if (bufferEnd <= sceneEndTime) return;

        double keepSeconds = (sceneEndTime - bufferStart).TotalSeconds;
        int keepSamples = (int)Math.Ceiling(keepSeconds * pcm.SampleRate);
        keepSamples = Math.Clamp(keepSamples, 0, pcm.NumSamples);

        if (keepSamples < pcm.NumSamples)
        {
            pcm.DataSpan[keepSamples..].Clear();
        }
    }

    private static void Swap<T>(ref T x, ref T y)
    {
        (y, x) = (x, y);
    }

    // オーディオバックエンドが最初のサンプルを出力するまで短いポーリングで待機する。
    // true を返した場合は実サンプルの観測に成功しており、呼び出し側は安全に AnchorClock できる。
    // 期限内に進行が観測できなかった場合は false を返し、呼び出し側は音声クロックを諦めて
    // 壁時計にフォールバックする (サスペンド/切断中のデバイスで無期限ハングするのを防ぐため)。
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
        AudioPlaybackClock clock, TimeSpan startTime, int sampleRate, int generation, CancellationToken playbackToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(playbackToken);
        cts.Token.ThrowIfCancellationRequested();
        TimeSpan cur = startTime;
        TimeSpan sceneEndTime = default;
        var fmt = new WaveFormat(sampleRate, 32, 2);
        var source = new XAudioSource(audioContext);
        var primaryBuffer = new XAudioBuffer();
        var secondaryBuffer = new XAudioBuffer();
        bool hasAudio = false;
        bool audioClockValid = false;

        void PrepareBuffer(XAudioBuffer buffer)
        {
            cts.Token.ThrowIfCancellationRequested();
            TimeSpan bufferStartTime = cur;
            var bufferData = FillAudioData(cur, scene, generation, playbackToken);
            sceneEndTime = bufferData.SceneEnd;
            using Pcm<Stereo32BitFloat>? pcm = bufferData.Pcm;
            cts.Token.ThrowIfCancellationRequested();
            if (pcm != null)
            {
                if (!hasAudio)
                {
                    sampleRate = pcm.SampleRate;
                    fmt = new WaveFormat(sampleRate, 32, 2);
                }

                buffer.BufferData(pcm.DataSpan, fmt);
                hasAudio = true;
                _sessionGuard.TryApply(generation, () => PublishAudioSnapshot(pcm, bufferStartTime));
            }

            source.QueueBuffer(buffer);
        }

        void AnchorClock()
        {
            if (!hasAudio || !audioClockValid) return;
            double seconds = (double)source.SamplesPlayed / sampleRate;
            clock.Anchor(startTime + TimeSpan.FromSeconds(seconds));
        }

        async Task PlaybackLoop()
        {
            PrepareBuffer(primaryBuffer);

            cur += s_second;
            PrepareBuffer(secondaryBuffer);

            source.Play();
            // Play() 直後は SamplesPlayed が 0 のままで、その時点でアンカーすると
            // 映像がウォールクロックで先行し、後続の AnchorClock で巻き戻しが発生する。
            // 実際のサンプル出力が始まるのを観測してからアンカーする。
            // タイムアウトした場合はバックエンドがハングしている可能性が高いので、
            // 音声クロックを諦めて壁時計にフォールバックする。
            audioClockValid = await WaitForFirstSampleAsync(
                    () => source.SamplesPlayed > 0, hasAudio, cts.Token)
                .ConfigureAwait(false);
            if (hasAudio && !audioClockValid)
            {
                _logger.LogWarning(
                    "XAudio2 backend did not advance SamplesPlayed within the startup deadline; falling back to wall-clock timing.");
            }
            AnchorClock();
            // 壁時計フォールバック時や音声なしの場合は AnchorClock が no-op のため、
            // ここで明示的に StartedTask をシグナルする。
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

                // バッファを入れ替える
                Swap(ref primaryBuffer, ref secondaryBuffer);

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
        AudioPlaybackClock clock, TimeSpan startTime, int sampleRate, int generation, CancellationToken playbackToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(playbackToken);
        cts.Token.ThrowIfCancellationRequested();
        audioContext.MakeCurrent();

        TimeSpan cur = startTime;
        TimeSpan sceneEndTime = default;
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
            clock.Anchor(startTime + TimeSpan.FromSeconds(seconds));
        }

        try
        {
            foreach (uint buffer in buffers)
            {
                if (cts.Token.IsCancellationRequested)
                    break;

                TimeSpan bufferStartTime = cur;
                var bufferData = FillAudioData(cur, scene, generation, playbackToken);
                sceneEndTime = bufferData.SceneEnd;
                using Pcm<Stereo32BitFloat>? pcmf = bufferData.Pcm;
                if (cts.Token.IsCancellationRequested)
                    break;

                cur += s_second;
                int fillSamples = 0;
                if (pcmf != null)
                {
                    using Pcm<Stereo16BitInteger> pcm = pcmf.Convert<Stereo16BitInteger>();

                    if (!hasAudio)
                    {
                        sampleRate = pcm.SampleRate;
                    }

                    audioContext.BufferData(buffer, BufferFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
                    fillSamples = pcm.DataSpan.Length;
                    hasAudio = true;
                    _sessionGuard.TryApply(generation, () => PublishAudioSnapshot(pcmf, bufferStartTime));
                }

                audioContext.SourceQueueBuffer(source, buffer);
                queuedBufferSamples.Enqueue(fillSamples);
            }

            cts.Token.ThrowIfCancellationRequested();
            audioContext.SourcePlay(source);

            try
            {
                // SourcePlay() 直後は SampleOffset が 0 のままで、その時点でアンカーすると
                // 映像がウォールクロックで先行し、後続の AnchorClock で巻き戻しが発生する。
                // 実際のサンプル出力が始まるのを観測してからアンカーする。
                // タイムアウトした場合はバックエンドがハングしている可能性が高いので、
                // 音声クロックを諦めて壁時計にフォールバックする。
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
                // 壁時計フォールバック時や音声なしの場合は AnchorClock が no-op のため、
                // ここで明示的に StartedTask をシグナルする。
                clock.SignalStarted();

                while (IsPlaying.Value && !cts.Token.IsCancellationRequested)
                {
                    audioContext.MakeCurrent();
                    audioContext.GetSource(source, GetSourceInteger.BuffersProcessed, out int processed);
                    while (processed > 0)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        TimeSpan bufferStartTime = cur;
                        var bufferData = FillAudioData(cur, scene, generation, playbackToken);
                        sceneEndTime = bufferData.SceneEnd;
                        using Pcm<Stereo32BitFloat>? pcmf = bufferData.Pcm;
                        cts.Token.ThrowIfCancellationRequested();
                        cur += s_second;
                        uint buffer = audioContext.SourceUnqueueBuffer(source);
                        int fillSamples = 0;
                        if (pcmf != null)
                        {
                            using Pcm<Stereo16BitInteger> pcm = pcmf.Convert<Stereo16BitInteger>();

                            if (!hasAudio)
                            {
                                sampleRate = pcm.SampleRate;
                            }

                            audioContext.BufferData(buffer, BufferFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
                            fillSamples = pcm.DataSpan.Length;
                            hasAudio = true;
                            _sessionGuard.TryApply(generation, () => PublishAudioSnapshot(pcmf, bufferStartTime));
                        }

                        audioContext.SourceQueueBuffer(source, buffer);

                        totalProcessedSamples += queuedBufferSamples.Dequeue();
                        queuedBufferSamples.Enqueue(fillSamples);
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
