using System.Reactive.Subjects;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Audio.Composing;
using Beutl.Audio.Platforms.OpenAL;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using OpenTK.Audio.OpenAL;
using Reactive.Bindings;
using SkiaSharp;
using Vortice.Multimedia;

namespace Beutl.ViewModels;

public sealed class PlayerViewModel : IAsyncDisposable
{
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);
    private readonly ILogger _logger = Log.CreateLogger<PlayerViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<bool> _isEnabled;
    private readonly EditViewModel _editViewModel;
    private IDisposable? _currentFrameSubscription;
    private CancellationTokenSource? _cts;
    private Size _maxFrameSize;
    private Task _playbackTask = Task.CompletedTask;

    public PlayerViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        Scene = editViewModel.Scene;
        _isEnabled = editViewModel.IsEnabled;
        PlayPause = new AsyncReactiveCommand(_isEnabled.AsObservable())
            .WithSubscribe(async () =>
            {
                if (IsPlaying.Value)
                {
                    await Pause();
                }
                else
                {
                    Play();
                }
            })
            .DisposeWith(_disposables);

        Next = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = GetFrameRate();
                UpdateCurrentFrame(EditViewModel.CurrentTime.Value + TimeSpan.FromSeconds(1d / rate));
            })
            .DisposeWith(_disposables);

        Previous = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = GetFrameRate();
                UpdateCurrentFrame(EditViewModel.CurrentTime.Value - TimeSpan.FromSeconds(1d / rate));
            })
            .DisposeWith(_disposables);

        Start = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() => EditViewModel.CurrentTime.Value = TimeSpan.Zero)
            .DisposeWith(_disposables);

        End = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() => EditViewModel.CurrentTime.Value = Scene.Duration)
            .DisposeWith(_disposables);

        Scene.Invalidated += OnSceneInvalidated;

        _isEnabled.Subscribe(async v =>
            {
                if (!v && IsPlaying.Value)
                {
                    await Pause();
                }
            })
            .DisposeWith(_disposables);

        CurrentFrame = EditViewModel.CurrentTime
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        _currentFrameSubscription = CurrentFrame.Subscribe(UpdateCurrentFrame);

        Duration = Scene.GetObservable(Scene.DurationProperty)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        PathEditor = new PathEditorViewModel(_editViewModel, this)
            .DisposeWith(_disposables);
    }

    private void OnSceneInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        if (e is TimelineInvalidatedEventArgs timelineInvalidated)
        {
            TimeSpan time = EditViewModel.CurrentTime.Value;
            if (!timelineInvalidated.AffectedRange.Any(v => v.Contains(time)))
            {
                return;
            }
        }

        QueueRender();
    }

    public Subject<Unit> AfterRendered { get; } = new();

    public Scene? Scene { get; set; }

    public Project? Project => Scene?.FindHierarchicalParent<Project>();

    public ReactivePropertySlim<Avalonia.Media.IImage> PreviewImage { get; } = new();

    public ReactivePropertySlim<bool> IsPlaying { get; } = new();

    public ReactiveProperty<TimeSpan> CurrentFrame { get; }

    public ReadOnlyReactiveProperty<TimeSpan> Duration { get; }

    public AsyncReactiveCommand PlayPause { get; }

    public ReactiveCommand Next { get; }

    public ReactiveCommand Previous { get; }

    public ReactiveCommand Start { get; }

    public ReactiveCommand End { get; }

    public ReactivePropertySlim<bool> IsMoveMode { get; } = new(true);

    public ReactivePropertySlim<bool> IsHandMode { get; } = new(false);

    public ReactivePropertySlim<bool> IsCropMode { get; } = new(false);

    public ReactivePropertySlim<Matrix> FrameMatrix { get; } = new(Matrix.Identity);

    public event EventHandler? PreviewInvalidated;

    // View側から設定
    public Size MaxFrameSize
    {
        get => _maxFrameSize;
        set
        {
            _maxFrameSize = value;

            if (_maxFrameSize != value)
            {
                FrameCacheManager frameCacheManager = EditViewModel.FrameCacheManager.Value;
                var frameSize = frameCacheManager.FrameSize.ToSize(1);
                float scale = (float)Stretch.Uniform.CalculateScaling(MaxFrameSize, frameSize, StretchDirection.Both).X;
                if (scale != 0)
                {
                    int den = (int)(1 / scale);
                    if (den % 2 == 1)
                    {
                        den++;
                    }

                    frameCacheManager.Options = frameCacheManager.Options with
                    {
                        Size = PixelSize.FromSize(frameSize, 1f / den)
                    };
                }
                else
                {
                    frameCacheManager.Options = frameCacheManager.Options with { Size = null };
                }
            }
        }
    }

    public Rect LastSelectedRect { get; set; }

    public EditViewModel EditViewModel => _editViewModel;

    public PathEditorViewModel PathEditor { get; }

    public void Play()
    {
        if (IsPlaying.Value) return;

        _playbackTask = Task.Run(async () =>
        {
            if (!_isEnabled.Value || Scene == null)
                return;

            BufferStatusViewModel bufferStatus = EditViewModel.BufferStatus;
            FrameCacheManager frameCacheManager = EditViewModel.FrameCacheManager.Value;
            Scene.Invalidated -= OnSceneInvalidated;
            _currentFrameSubscription?.Dispose();

            IsPlaying.Value = true;
            int rate = GetFrameRate();

            TimeSpan tick = TimeSpan.FromSeconds(1d / rate);
            TimeSpan startTime = EditViewModel.CurrentTime.Value;
            TimeSpan durationTime = Scene.Duration;
            int startFrame = (int)startTime.ToFrameNumber(rate);
            int durationFrame = (int)Math.Ceiling(durationTime.ToFrameNumber(rate));
            bufferStatus.StartTime.Value = startTime;
            bufferStatus.EndTime.Value = startTime;
            frameCacheManager.Options = frameCacheManager.Options with
            {
                DeletionStrategy = FrameCacheDeletionStrategy.BackwardBlock
            };

            frameCacheManager.CurrentFrame = startFrame;
            using var playerImpl = new BufferedPlayer(EditViewModel, Scene, IsPlaying, rate);
            _logger.LogInformation("Start the playback. ({SceneId}, {Rate}, {Start}, {Duration})",
                _editViewModel.SceneId, rate, startFrame, durationFrame);
            playerImpl.Start();
            //
            // if (!await _audioSemaphoreSlim.WaitAsync(1000))
            // {
            //     NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred,
            //         Message.An_exception_occurred_during_audio_playback);
            //     _logger.LogWarning("Failed to acquire the semaphore for audio playback.");
            //     return;
            // }

            var audioTask = PlayAudio(Scene);

            DateTime startDateTime = DateTime.UtcNow;
            var tcs = new TaskCompletionSource<bool>();
            int nextExpectedFrame = startFrame + 1;
            bool processing = false;
            await using var timer = new Timer(_ =>
            {
                if (processing) return;
                processing = true;
                try
                {
                    var expectFrame = (int)((DateTime.UtcNow - startDateTime).Ticks / tick.Ticks) + startFrame;
                    if (!IsPlaying.Value || expectFrame >= durationFrame)
                    {
                        tcs.TrySetResult(true);
                        return;
                    }

                    if (expectFrame < nextExpectedFrame)
                    {
                        return;
                    }

                    while (playerImpl.TryDequeue(out IPlayer.Frame frame))
                    {
                        using (frame.Bitmap)
                        {
                            UpdateImage(frame.Bitmap.Value);

                            if (Scene != null)
                            {
                                EditViewModel.CurrentTime.Value = frame.Time.ToTimeSpan(rate);
                                EditViewModel.FrameCacheManager.Value.CurrentFrame = frame.Time;
                            }
                        }

                        // タイマーが正確じゃないから、だんだんとフレームがずれてくる
                        // そのため、フレームを消費しすぎたら、そのフレーム番号とexpectFrameが一致するまでスキップする
                        // 逆に、フレームを消費しすぎない場合は、そのまま次のフレームを取得する
                        if (expectFrame <= frame.Time)
                        {
                            nextExpectedFrame = frame.Time + 1;
                            break;
                        }

                        // 期待していたフレームよりも前のフレームが来た場合
                    }

                    playerImpl.Skipped(
                        (int)((DateTime.UtcNow - startDateTime).Ticks / tick.Ticks) + startFrame + 1);
                }
                finally
                {
                    processing = false;
                }
            }, null, tick, tick);

            await Task.WhenAll(tcs.Task, audioTask);

            IsPlaying.Value = false;
            frameCacheManager.UpdateBlocks();
            bufferStatus.StartTime.Value = TimeSpan.Zero;
            bufferStatus.EndTime.Value = TimeSpan.Zero;
            frameCacheManager.Options = frameCacheManager.Options with
            {
                DeletionStrategy = FrameCacheDeletionStrategy.Old
            };

            _currentFrameSubscription = CurrentFrame.Subscribe(UpdateCurrentFrame);
            Scene.Invalidated += OnSceneInvalidated;
            _logger.LogInformation("End the playback. ({SceneId})", _editViewModel.SceneId);
        });
    }

    public int GetFrameRate()
    {
        int rate = Project?.GetFrameRate() ?? 30;
        if (rate <= 0)
        {
            rate = 30;
        }

        return rate;
    }

    private async Task PlayAudio(Scene scene)
    {
        if (OperatingSystem.IsWindows())
        {
            using var audioContext = new XAudioContext();
            await PlayWithXA2(audioContext, scene).ConfigureAwait(false);
        }
        else
        {
            await Task.Run(async () =>
            {
                using var audioContext = new AudioContext();
                await PlayWithOpenAL(audioContext, scene);
            });
        }
    }

    private static Pcm<Stereo32BitFloat>? FillAudioData(TimeSpan f, IComposer composer)
    {
        if (composer.Compose(f) is { } pcm)
        {
            return pcm;
        }
        else
        {
            return null;
        }
    }

    private static void Swap<T>(ref T x, ref T y)
    {
        (y, x) = (x, y);
    }

    private async Task PlayWithXA2(XAudioContext audioContext, Scene scene)
    {
        IComposer composer = EditViewModel.Composer.Value;
        int sampleRate = composer.SampleRate;
        TimeSpan cur = EditViewModel.CurrentTime.Value;
        var fmt = new WaveFormat(sampleRate, 32, 2);
        var source = new XAudioSource(audioContext);
        var primaryBuffer = new XAudioBuffer();
        var secondaryBuffer = new XAudioBuffer();

        void PrepareBuffer(XAudioBuffer buffer)
        {
            Pcm<Stereo32BitFloat>? pcm = FillAudioData(cur, composer);
            if (pcm != null)
            {
                buffer.BufferData(pcm.DataSpan, fmt);
            }

            source.QueueBuffer(buffer);
        }

        var cts = new CancellationTokenSource();
        IDisposable revoker = IsPlaying.Where(v => !v)
            .Subscribe(_ =>
            {
                // ReSharper disable AccessToDisposedClosure
                source.Stop();
                cts.Cancel();
                // ReSharper restore AccessToDisposedClosure
            });

        try
        {
            PrepareBuffer(primaryBuffer);

            cur += s_second;
            PrepareBuffer(secondaryBuffer);

            source.Play();

            await Task.Delay(1000, cts.Token).ConfigureAwait(false);

            // primaryBufferが終了、secondaryが開始

            while (cur < scene.Duration)
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
            }
        }
        catch (OperationCanceledException)
        {
            source.Stop();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred,
                Message.An_exception_occurred_during_audio_playback);
            _logger.LogError(ex, "An exception occurred during audio playback.");
            IsPlaying.Value = false;
        }
        finally
        {
            revoker.Dispose();
            source.Dispose();
            cts.Dispose();
            primaryBuffer.Dispose();
            secondaryBuffer.Dispose();
        }
    }

    private async Task PlayWithOpenAL(AudioContext audioContext, Scene scene)
    {
        static void CheckError()
        {
            ALError error = AL.GetError();

            if (error is not ALError.NoError)
            {
                throw new Exception(AL.GetErrorString(error));
            }
        }

        var cts = new CancellationTokenSource();
        IDisposable revoker = IsPlaying.Where(v => !v)
            .Subscribe(_ => cts.Cancel());

        try
        {
            audioContext.MakeCurrent();

            IComposer composer = EditViewModel.Composer.Value;
            TimeSpan cur = EditViewModel.CurrentTime.Value;
            int[] buffers = AL.GenBuffers(2);
            CheckError();
            int source = AL.GenSource();
            CheckError();

            foreach (int buffer in buffers)
            {
                using Pcm<Stereo32BitFloat>? pcmf = FillAudioData(cur, composer);
                cur += s_second;
                if (pcmf != null)
                {
                    using Pcm<Stereo16BitInteger> pcm = pcmf.Convert<Stereo16BitInteger>();

                    AL.BufferData<Stereo16BitInteger>(buffer, ALFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
                    CheckError();
                }

                AL.SourceQueueBuffer(source, buffer);
                CheckError();
            }

            AL.SourcePlay(source);
            CheckError();

            try
            {
                while (IsPlaying.Value)
                {
                    audioContext.MakeCurrent();
                    AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int processed);
                    CheckError();
                    while (processed > 0)
                    {
                        using Pcm<Stereo32BitFloat>? pcmf = FillAudioData(cur, composer);
                        cur += s_second;
                        int buffer = AL.SourceUnqueueBuffer(source);
                        CheckError();
                        if (pcmf != null)
                        {
                            using Pcm<Stereo16BitInteger> pcm = pcmf.Convert<Stereo16BitInteger>();

                            AL.BufferData<Stereo16BitInteger>(buffer, ALFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
                            CheckError();
                        }

                        AL.SourceQueueBuffer(source, buffer);
                        CheckError();
                        processed--;
                    }

                    if (AL.GetSourceState(source) != ALSourceState.Playing)
                    {
                        CheckError();
                        AL.SourcePlay(source);
                        CheckError();
                    }

                    await Task.Delay(100, cts.Token).ConfigureAwait(false);
                    if (cur > scene.Duration)
                        break;
                }

                while (AL.GetSourceState(source) == ALSourceState.Playing && IsPlaying.Value)
                {
                    await Task.Delay(100, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }

            CheckError();
            AL.SourceStop(source);
            CheckError();
            // https://hamken100.blogspot.com/2014/04/aldeletebuffersalinvalidoperation.html
            AL.Source(source, ALSourcei.Buffer, 0);
            CheckError();
            AL.DeleteBuffers(buffers);
            CheckError();
            AL.DeleteSource(source);
            CheckError();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred,
                Message.An_exception_occurred_during_audio_playback);
            _logger.LogError(ex, "An exception occurred during audio playback.");
            IsPlaying.Value = false;
        }

        revoker.Dispose();
    }

    public async Task Pause()
    {
        if (!IsPlaying.Value) return;

        _logger.LogInformation("Pause the playback. ({SceneId})", _editViewModel.SceneId);
        IsPlaying.Value = false;
        await _playbackTask;
    }

    private unsafe void UpdateImage(Bitmap<Bgra8888> source)
    {
        WriteableBitmap bitmap;

        if (PreviewImage.Value is WriteableBitmap bitmap1 &&
            bitmap1.PixelSize.Width == source.Width &&
            bitmap1.PixelSize.Height == source.Height)
        {
            bitmap = bitmap1;
        }
        else
        {
            bitmap = new WriteableBitmap(
                new(source.Width, source.Height),
                new(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
        }

        PreviewImage.Value = bitmap;
        using (ILockedFramebuffer buf = bitmap.Lock())
        {
            int size = source.ByteCount;
            Buffer.MemoryCopy((void*)source.Data, (void*)buf.Address, size, size);
        }

        PreviewInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void DrawBoundaries(Renderer renderer, ImmediateCanvas canvas)
    {
        int? selected = EditViewModel.SelectedLayerNumber.Value;
        if (selected.HasValue)
        {
            var frameSize = new Size(renderer.FrameSize.Width, renderer.FrameSize.Height);
            float scale = (float)Stretch.Uniform.CalculateScaling(MaxFrameSize, frameSize, StretchDirection.Both).X;
            if (scale == 0)
                scale = 1;

            Rect[] boundary = renderer.RenderScene[selected.Value].GetBoundaries();
            if (boundary.Length > 0)
            {
                var pen = new Media.Immutable.ImmutablePen(Brushes.White, null, 0, 1 / scale);
                bool exactBounds = GlobalConfiguration.Instance.ViewConfig.ShowExactBoundaries;

                foreach (Rect item in boundary)
                {
                    Rect rect = item;
                    if (!exactBounds)
                    {
                        rect = item.Inflate(4 / scale);
                    }

                    canvas.DrawRectangle(rect, null, pen);
                }
            }
        }
    }

    private void QueueRender()
    {
        if (EditViewModel.Renderer.Value.IsGraphicsRendering)
            return;

        void RenderOnRenderThread(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            RenderThread.Dispatcher.Dispatch(() =>
            {
                try
                {
                    SceneRenderer renderer = EditViewModel.Renderer.Value;
                    FrameCacheManager cacheManager = EditViewModel.FrameCacheManager.Value;
                    if (renderer is not { IsDisposed: false, IsGraphicsRendering: false })
                        return;

                    int rate = GetFrameRate();
                    TimeSpan time = EditViewModel.CurrentTime.Value;
                    int frame = (int)Math.Round(time.ToFrameNumber(rate), MidpointRounding.AwayFromZero);
                    time = TimeSpanExtensions.ToTimeSpan(frame, rate);
                    Bitmap<Bgra8888>? bitmap = null;

                    if (cacheManager.TryGet(frame, out var cache))
                    {
                        using (cache)
                        {
                            renderer.RenderScene.Clear();
                            renderer.Evaluate(time);

                            ImmediateCanvas canvas = Renderer.GetInternalCanvas(renderer);
                            canvas.Clear();

                            using (canvas.PushTransform(
                                       Matrix.CreateScale(canvas.Size.Width / (float)cache.Value.Width,
                                           canvas.Size.Height / (float)cache.Value.Height)))
                            {
                                canvas.DrawBitmap(cache.Value, Brushes.White, null);
                            }

                            DrawBoundaries(renderer, canvas);

                            bitmap = renderer.Snapshot();
                        }
                    }
                    else if (renderer.Render(time))
                    {
                        using (var forCache = Ref<Bitmap<Bgra8888>>.Create(renderer.Snapshot()))
                        {
                            cacheManager.Add(frame, forCache);
                            cacheManager.UpdateBlocks();
                        }

                        ImmediateCanvas canvas = Renderer.GetInternalCanvas(renderer);
                        DrawBoundaries(renderer, canvas);

                        bitmap = renderer.Snapshot();
                    }

                    if (bitmap != null)
                    {
                        using (bitmap)
                        {
                            UpdateImage(bitmap);
                        }
                    }

                    AfterRendered.OnNext(Unit.Default);
                }
                catch (Exception ex)
                {
                    NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred,
                        Message.An_exception_occurred_while_drawing_frame);
                    _logger.LogError(ex, "An exception occurred while drawing the frame.");
                }
            }, ct: token);
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => RenderOnRenderThread(_cts.Token),
            Avalonia.Threading.DispatcherPriority.Background,
            _cts.Token);
    }

    private void UpdateCurrentFrame(TimeSpan timeSpan)
    {
        QueueRender();

        if (Scene == null) return;

        if (EditViewModel.CurrentTime.Value != timeSpan)
        {
            //int rate = Project.GetFrameRate();
            //timeSpan = timeSpan.FloorToRate(rate);

            //if (timeSpan >= Scene.Duration)
            //{
            //    timeSpan = Scene.Duration - TimeSpan.FromSeconds(1d / rate);
            //}
            //if (timeSpan < TimeSpan.Zero)
            //{
            //    timeSpan = TimeSpan.Zero;
            //}

            EditViewModel.CurrentTime.Value = timeSpan;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing PlayerViewModel. ({SceneId})", _editViewModel.SceneId);
        await Pause();
        Scene!.Invalidated -= OnSceneInvalidated;
        _disposables.Dispose();
        _currentFrameSubscription?.Dispose();
        AfterRendered.Dispose();
        PreviewInvalidated = null;
        Scene = null!;
        PreviewImage.Value = null!;
        _logger.LogInformation("Disposed PlayerViewModel. ({SceneId})", _editViewModel.SceneId);
    }

    public async Task<Rect> StartSelectRect()
    {
        TcsForCrop = new TaskCompletionSource<Rect>();
        IsCropMode.Value = true;
        Rect r = await TcsForCrop.Task;
        TcsForCrop = null;
        return r;
    }

    public TaskCompletionSource<Rect>? TcsForCrop { get; private set; }

    public async Task<Bitmap<Bgra8888>> DrawSelectedDrawable(Drawable drawable)
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");
            // TODO: Rendererに特定のDrawableのみを描画するクラスを追加する
            SceneRenderer renderer = EditViewModel.Renderer.Value;
            PixelSize frameSize = renderer.FrameSize;
            using var root = new DrawableRenderNode(drawable);
            using (var context = new GraphicsContext2D(root, frameSize))
            {
                drawable.Render(context);
            }

            var processor = new RenderNodeProcessor(root, false);
            return processor.RasterizeAndConcat();
        });
    }

    public async Task<Bitmap<Bgra8888>> DrawFrame()
    {
        await Pause();

        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");
            SceneRenderer renderer = EditViewModel.Renderer.Value;

            RenderNodeCacheContext cacheContext = renderer.GetCacheContext();
            RenderCacheOptions restoreCacheOptions = cacheContext.CacheOptions;
            cacheContext.CacheOptions = RenderCacheOptions.Disabled;

            try
            {
                if (!renderer.Render(CurrentFrame.Value))
                {
                    throw new Exception("Failed to render.");
                }

                return renderer.Snapshot();
            }
            finally
            {
                cacheContext.CacheOptions = restoreCacheOptions;
            }
        });
    }
}
