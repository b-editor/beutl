﻿using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Beutl.Audio.Platforms.OpenAL;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Rendering;
using Beutl.Rendering.Cache;
using Beutl.Services;

using Microsoft.Extensions.Logging;

using OpenTK.Audio.OpenAL;

using Reactive.Bindings;

using SkiaSharp;

using Vortice.Multimedia;

namespace Beutl.ViewModels;

public sealed class PlayerViewModel : IDisposable
{
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);
    private readonly ILogger _logger = Log.CreateLogger<PlayerViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<bool> _isEnabled;
    private readonly EditViewModel _editViewModel;
    private IDisposable? _currentFrameSubscription;
    private CancellationTokenSource? _cts;
    private Size _maxFrameSize;

    public PlayerViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        Scene = editViewModel.Scene;
        _isEnabled = editViewModel.IsEnabled;
        PlayPause = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                if (IsPlaying.Value)
                {
                    Pause();
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
                UpdateCurrentFrame(_editViewModel.CurrentTime.Value + TimeSpan.FromSeconds(1d / rate));
            })
            .DisposeWith(_disposables);

        Previous = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = GetFrameRate();
                UpdateCurrentFrame(_editViewModel.CurrentTime.Value - TimeSpan.FromSeconds(1d / rate));
            })
            .DisposeWith(_disposables);

        Start = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() => _editViewModel.CurrentTime.Value = TimeSpan.Zero)
            .DisposeWith(_disposables);

        End = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() => _editViewModel.CurrentTime.Value = Scene.Duration)
            .DisposeWith(_disposables);

        Scene.Invalidated += OnSceneInvalidated;

        _isEnabled.Subscribe(v =>
            {
                if (!v && IsPlaying.Value)
                {
                    Pause();
                }
            })
            .DisposeWith(_disposables);

        CurrentFrame = _editViewModel.CurrentTime
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        _currentFrameSubscription = CurrentFrame.Subscribe(UpdateCurrentFrame);

        Duration = Scene.GetObservable(Scene.DurationProperty)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);
    }

    private void OnSceneInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        if (e is TimelineInvalidatedEventArgs timelineInvalidated)
        {
            TimeSpan time = _editViewModel.CurrentTime.Value;
            if (!timelineInvalidated.AffectedRange.Any(v => v.Contains(time)))
            {
                return;
            }
        }

        QueueRender();
    }

    public Scene? Scene { get; set; }

    public Project? Project => Scene?.FindHierarchicalParent<Project>();

    public ReactivePropertySlim<Avalonia.Media.IImage> PreviewImage { get; } = new();

    public ReactivePropertySlim<bool> IsPlaying { get; } = new();

    public ReactiveProperty<TimeSpan> CurrentFrame { get; }

    public ReadOnlyReactiveProperty<TimeSpan> Duration { get; }

    public ReactiveCommand PlayPause { get; }

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
                FrameCacheManager frameCacheManager = _editViewModel.FrameCacheManager.Value;
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

                    frameCacheManager.Options = frameCacheManager.Options with
                    {
                        Size = null
                    };
                }
            }
        }
    }

    public Rect LastSelectedRect { get; set; }

    public void Play()
    {
        Task.Run(async () =>
        {
            if (!_isEnabled.Value || Scene == null)
                return;

            IRenderer renderer = _editViewModel.Renderer.Value;
            BufferStatusViewModel bufferStatus = _editViewModel.BufferStatus;
            FrameCacheManager frameCacheManager = _editViewModel.FrameCacheManager.Value;
            Scene.Invalidated -= OnSceneInvalidated;
            _currentFrameSubscription?.Dispose();

            try
            {
                IsPlaying.Value = true;
                int rate = GetFrameRate();

                TimeSpan tick = TimeSpan.FromSeconds(1d / rate);
                TimeSpan startTime = _editViewModel.CurrentTime.Value;
                TimeSpan durationTime = Scene.Duration;
                int startFrame = (int)startTime.ToFrameNumber(rate);
                int durationFrame = (int)Math.Ceiling(durationTime.ToFrameNumber(rate));
                var tcs = new TaskCompletionSource();
                bufferStatus.StartTime.Value = startTime;
                bufferStatus.EndTime.Value = startTime;
                frameCacheManager.Options = frameCacheManager.Options with
                {
                    DeletionStrategy = FrameCacheDeletionStrategy.BackwardBlock
                };

                frameCacheManager.CurrentFrame = startFrame;
                using var playerImpl = new BufferedPlayer(_editViewModel, Scene, IsPlaying, rate);
                playerImpl.Start();

                PlayAudio(Scene);

                using var timer = new System.Timers.Timer(tick);
                bool timerProcessing = false;
                timer.Elapsed += (_, e) =>
                {
                    startFrame++;

                    if (startFrame >= durationFrame || !IsPlaying.Value)
                    {
                        timer.Stop();
                        tcs.TrySetResult();
                        return;
                    }

                    if (timerProcessing)
                    {
                        playerImpl.Skipped(startFrame);
                        return;
                    }

                    try
                    {
                        timerProcessing = true;

                        if (playerImpl.TryDequeue(out IPlayer.Frame frame))
                        {
                            // 所有権が移転したので
                            using (frame.Bitmap)
                            {
                                UpdateImage(frame.Bitmap.Value);

                                if (Scene != null)
                                {
                                    _editViewModel.CurrentTime.Value = TimeSpanExtensions.ToTimeSpan(frame.Time, rate);
                                    _editViewModel.FrameCacheManager.Value.CurrentFrame = frame.Time;
                                }
                            }
                        }
                    }
                    finally
                    {
                        timerProcessing = false;
                    }
                };
                timer.Start();

                await tcs.Task;
                frameCacheManager.UpdateBlocks();
                IsPlaying.Value = false;
                bufferStatus.StartTime.Value = TimeSpan.Zero;
                bufferStatus.EndTime.Value = TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                // 本来ここには例外が来ないはず
                _logger.LogError(ex, "An exception occurred during the playback process.");
            }
            finally
            {
                frameCacheManager.Options = frameCacheManager.Options with
                {
                    DeletionStrategy = FrameCacheDeletionStrategy.Old
                };

                _currentFrameSubscription = CurrentFrame.Subscribe(UpdateCurrentFrame);
                Scene.Invalidated += OnSceneInvalidated;
            }
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

    private async void PlayAudio(Scene scene)
    {
        if (OperatingSystem.IsWindows())
        {
            using var audioContext = new XAudioContext();
            await PlayWithXA2(audioContext, scene).ConfigureAwait(false);
        }
        else
        {
            using var audioContext = new AudioContext();
            await PlayWithOpenAL(audioContext, scene).ConfigureAwait(false);
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
        IComposer composer = _editViewModel.Composer.Value;
        int sampleRate = composer.SampleRate;
        TimeSpan cur = _editViewModel.CurrentTime.Value;
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

        IDisposable revoker = IsPlaying.Where(v => !v)
            .Subscribe(_ => source.Stop());

        try
        {
            PrepareBuffer(primaryBuffer);

            cur += s_second;
            PrepareBuffer(secondaryBuffer);

            source.Play();

            await Task.Delay(1000).ConfigureAwait(false);

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

                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred, Message.An_exception_occurred_during_audio_playback);
            _logger.LogError(ex, "An exception occurred during audio playback.");
            IsPlaying.Value = false;
        }
        finally
        {
            revoker.Dispose();
            source.Dispose();
            primaryBuffer.Dispose();
            secondaryBuffer.Dispose();
        }
    }

    private async Task PlayWithOpenAL(AudioContext audioContext, Scene scene)
    {
        try
        {
            audioContext.MakeCurrent();

            IComposer composer = _editViewModel.Composer.Value;
            TimeSpan cur = _editViewModel.CurrentTime.Value;
            int[] buffers = AL.GenBuffers(2);
            int source = AL.GenSource();

            foreach (int buffer in buffers)
            {
                using Pcm<Stereo32BitFloat>? pcmf = FillAudioData(cur, composer);
                cur += s_second;
                if (pcmf != null)
                {
                    using Pcm<Stereo16BitInteger> pcm = pcmf.Convert<Stereo16BitInteger>();

                    AL.BufferData<Stereo16BitInteger>(buffer, ALFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
                }

                AL.SourceQueueBuffer(source, buffer);
            }

            AL.SourcePlay(source);

            while (IsPlaying.Value)
            {
                AL.GetSource(source, ALGetSourcei.BuffersProcessed, out int processed);
                while (processed > 0)
                {
                    using Pcm<Stereo32BitFloat>? pcmf = FillAudioData(cur, composer);
                    cur += s_second;
                    int buffer = AL.SourceUnqueueBuffer(source);
                    if (pcmf != null)
                    {
                        using Pcm<Stereo16BitInteger> pcm = pcmf.Convert<Stereo16BitInteger>();

                        AL.BufferData<Stereo16BitInteger>(buffer, ALFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
                    }

                    AL.SourceQueueBuffer(source, buffer);
                    processed--;
                }

                await Task.Delay(1000).ConfigureAwait(false);
                if (cur > scene.Duration)
                    break;
            }

            while (AL.GetSourceState(source) == ALSourceState.Playing)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }

            AL.DeleteBuffers(buffers);
            AL.DeleteSource(source);
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred, Message.An_exception_occurred_during_audio_playback);
            _logger.LogError(ex, "An exception occurred during audio playback.");
            IsPlaying.Value = false;
        }
    }

    public void Pause()
    {
        IsPlaying.Value = false;
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
        int? selected = _editViewModel.SelectedLayerNumber.Value;
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
        if (_editViewModel.Renderer.Value.IsGraphicsRendering)
            return;

        void RenderOnRenderThread(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            RenderThread.Dispatcher.Dispatch(() =>
            {
                try
                {
                    SceneRenderer renderer = _editViewModel.Renderer.Value;
                    FrameCacheManager cacheManager = _editViewModel.FrameCacheManager.Value;
                    if (renderer is not { IsDisposed: false, IsGraphicsRendering: false })
                        return;

                    int rate = GetFrameRate();
                    TimeSpan time = _editViewModel.CurrentTime.Value;
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
                                Matrix.CreateScale(canvas.Size.Width / (float)cache.Value.Width, canvas.Size.Height / (float)cache.Value.Height)))
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
                }
                catch (Exception ex)
                {
                    NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred, Message.An_exception_occurred_while_drawing_frame);
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

        if (_editViewModel.CurrentTime.Value != timeSpan)
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

            _editViewModel.CurrentTime.Value = timeSpan;
        }
    }

    public void Dispose()
    {
        Pause();
        _disposables.Dispose();
        _currentFrameSubscription?.Dispose();
        PreviewInvalidated = null;
        Scene = null!;
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

    public Task<Bitmap<Bgra8888>> DrawSelectedDrawable(Drawable drawable)
    {
        Pause();

        return RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");
            IRenderer renderer = _editViewModel.Renderer.Value;
            PixelSize frameSize = renderer.FrameSize;
            using var root = new DrawableNode(drawable);
            using var dcanvas = new DeferradCanvas(root, frameSize);
            drawable.Render(dcanvas);

            Rect bounds = root.Bounds;
            var rect = PixelRect.FromRect(bounds);
            using SKSurface? surface = renderer.CreateRenderTarget(rect.Width, rect.Height)
                ?? throw new Exception("surface is null");

            using ImmediateCanvas icanvas = renderer.CreateCanvas(surface, true);

            RenderCacheContext? cacheContext = renderer.GetCacheContext();
            RenderCacheOptions? restoreCacheOptions = null;

            if (cacheContext != null)
            {
                restoreCacheOptions = cacheContext.CacheOptions;
                cacheContext.CacheOptions = RenderCacheOptions.Disabled;
            }

            try
            {
                using (icanvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
                {
                    icanvas.DrawNode(root);
                }

                return icanvas.GetBitmap();
            }
            finally
            {
                if (cacheContext != null && restoreCacheOptions != null)
                {
                    cacheContext.CacheOptions = restoreCacheOptions;
                }
            }
        });
    }

    public Task<Bitmap<Bgra8888>> DrawFrame()
    {
        Pause();

        return RenderThread.Dispatcher.InvokeAsync(() =>
        {
            if (Scene == null) throw new Exception("Scene is null.");
            IRenderer renderer = _editViewModel.Renderer.Value;

            RenderCacheContext? cacheContext = renderer.GetCacheContext();
            RenderCacheOptions? restoreCacheOptions = null;

            if (cacheContext != null)
            {
                restoreCacheOptions = cacheContext.CacheOptions;
                cacheContext.CacheOptions = RenderCacheOptions.Disabled;
            }

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
                if (cacheContext != null && restoreCacheOptions != null)
                {
                    cacheContext.CacheOptions = restoreCacheOptions;
                }
            }
        });
    }
}
