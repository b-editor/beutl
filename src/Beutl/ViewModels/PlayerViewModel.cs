using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Beutl.Audio;
using Beutl.Audio.Platforms.OpenAL;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.Rendering;

using OpenTK.Audio.OpenAL;

using Reactive.Bindings;

using Vortice.Multimedia;

namespace Beutl.ViewModels;

public sealed class PlayerViewModel : IDisposable
{
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);
    private readonly IDisposable _disposable0;
    private readonly IDisposable _disposable1;
    private readonly ReactivePropertySlim<bool> _isEnabled;

    public PlayerViewModel(Scene scene, ReactivePropertySlim<bool> isEnabled)
    {
        Scene = scene;
        _isEnabled = isEnabled;
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
            });

        Next = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = Project.GetFrameRate();
                if (rate <= 0)
                {
                    rate = 30;
                }

                Scene.CurrentFrame += TimeSpan.FromSeconds(1d / rate);
            });

        Previous = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() =>
            {
                int rate = Project.GetFrameRate();
                if (rate <= 0)
                {
                    rate = 30;
                }

                Scene.CurrentFrame -= TimeSpan.FromSeconds(1d / rate);
            });

        Start = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() => Scene.CurrentFrame = TimeSpan.Zero);

        End = new ReactiveCommand(_isEnabled)
            .WithSubscribe(() => Scene.CurrentFrame = Scene.Duration);

        Scene.Renderer.RenderInvalidated += Renderer_RenderInvalidated;
        _disposable0 = Scene.GetPropertyChangedObservable(Scene.RendererProperty)
            .Subscribe(a =>
            {
                if (a.OldValue != null)
                {
                    a.OldValue.RenderInvalidated -= Renderer_RenderInvalidated;
                }

                if (a.NewValue != null)
                {
                    a.NewValue.RenderInvalidated += Renderer_RenderInvalidated;
                }
            });

        _disposable1 = _isEnabled.Subscribe(v =>
        {
            if (!v && IsPlaying.Value)
            {
                Pause();
            }
        });
    }

    public Scene Scene { get; }

    public Project Project => Scene.FindRequiredLogicalParent<Project>();

    public ReactivePropertySlim<IImage> PreviewImage { get; } = new();

    public ReactivePropertySlim<bool> IsPlaying { get; } = new();

    public ReactiveCommand PlayPause { get; }

    public ReactiveCommand Next { get; }

    public ReactiveCommand Previous { get; }

    public ReactiveCommand Start { get; }

    public ReactiveCommand End { get; }

    public event EventHandler? PreviewInvalidated;

    public async void Play()
    {
        IRenderer renderer = Scene.Renderer;
        renderer.RenderInvalidated -= Renderer_RenderInvalidated;

        IsPlaying.Value = true;
        int rate = Project.GetFrameRate();
        if (rate >= 0)
        {
            rate = 30;
        }

        PlayAudio();

        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);
        TimeSpan curFrame = Scene.CurrentFrame;
        TimeSpan duration = Scene.Duration;

        using (var timer = new PeriodicTimer(tick))
        {
            DateTime dateTime = DateTime.UtcNow;
            while (await timer.WaitForNextTickAsync()
                && curFrame <= duration
                && IsPlaying.Value)
            {
                curFrame += tick;
                Render(renderer, curFrame);
            }
        }

        renderer.RenderInvalidated += Renderer_RenderInvalidated;
    }

    private async void PlayAudio()
    {
        if (OperatingSystem.IsWindows())
        {
            using var audioContext = new XAudioContext();
            await PlayWithXA2(audioContext);
        }
        else
        {
            using var audioContext = new AudioContext();
            PlayWithOpenAL(audioContext);
        }
    }

    private static Pcm<Stereo32BitFloat>? FillAudioData(TimeSpan f, IRenderer renderer)
    {
        if (renderer.RenderAudio(f).Audio is { } pcm)
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
        T temp = x;
        x = y;
        y = temp;
    }

    private async Task PlayWithXA2(XAudioContext audioContext)
    {
        IRenderer renderer = Scene.Renderer;
        int sampleRate = renderer.Audio.SampleRate;
        TimeSpan cur = Scene.CurrentFrame;
        var fmt = new WaveFormat(sampleRate, 32, 2);
        var source = new XAudioSource(audioContext);
        var primaryBuffer = new XAudioBuffer();
        var secondaryBuffer = new XAudioBuffer();

        void PrepareBuffer(XAudioBuffer buffer)
        {
            Pcm<Stereo32BitFloat>? pcm = FillAudioData(cur, renderer);
            if (pcm != null)
            {
                buffer.BufferData(pcm.DataSpan, fmt);
            }
            source.QueueBuffer(buffer);
        }

        try
        {
            PrepareBuffer(primaryBuffer);

            cur += s_second;
            PrepareBuffer(secondaryBuffer);

            source.Play();

            await Task.Delay(1000).ConfigureAwait(false);
            // primaryBufferが終了、secondaryが開始

            while (cur < Scene.Duration)
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
        finally
        {
            source.Dispose();
            primaryBuffer.Dispose();
            secondaryBuffer.Dispose();
        }
    }

    private async void PlayWithOpenAL(AudioContext audioContext)
    {
        audioContext.MakeCurrent();

        IRenderer renderer = Scene.Renderer;
        TimeSpan cur = Scene.CurrentFrame;
        var buffers = AL.GenBuffers(2);
        var source = AL.GenSource();

        foreach (var buffer in buffers)
        {
            using var pcmf = FillAudioData(cur, renderer);
            cur += s_second;
            if (pcmf != null)
            {
                using var pcm = pcmf.Convert<Stereo16BitInteger>();

                AL.BufferData<Stereo16BitInteger>(buffer, ALFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
            }

            AL.SourceQueueBuffer(source, buffer);
        }

        AL.SourcePlay(source);

        while (IsPlaying.Value)
        {
            AL.GetSource(source, ALGetSourcei.BuffersProcessed, out var processed);
            while (processed > 0)
            {
                using Pcm<Stereo32BitFloat>? pcmf = FillAudioData(cur, renderer);
                cur += s_second;
                int buffer = AL.SourceUnqueueBuffer(source);
                if (pcmf != null)
                {
                    using var pcm = pcmf.Convert<Stereo16BitInteger>();

                    AL.BufferData<Stereo16BitInteger>(buffer, ALFormat.Stereo16, pcm.DataSpan, pcm.SampleRate);
                }

                AL.SourceQueueBuffer(source, buffer);
                processed--;
            }

            if (cur > Scene.Duration)
                break;
        }

        while (AL.GetSourceState(source) == ALSourceState.Playing)
        {
            await Task.Delay(100);
        }

        AL.DeleteBuffers(buffers);
        AL.DeleteSource(source);
    }

    public void Pause()
    {
        IsPlaying.Value = false;
    }

    private void Render(IRenderer renderer, TimeSpan timeSpan)
    {
        if (renderer.IsGraphicsRendering && !_isEnabled.Value)
            return;

        renderer.Dispatcher.Invoke(() =>
        {
            if (renderer.RenderGraphics(timeSpan).Bitmap is { } bitmap)
            {
                UpdateImage(bitmap);
                bitmap.Dispose();

                Scene.CurrentFrame = timeSpan;
            }
        });
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

    private unsafe void Renderer_RenderInvalidated(object? sender, IRenderer.RenderResult e)
    {
        if (e.Bitmap is { } bitmap)
        {
            UpdateImage(bitmap);
        }
    }

    public void Dispose()
    {
        _disposable0.Dispose();
        _disposable1.Dispose();
    }
}
