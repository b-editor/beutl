using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.Rendering;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class PlayerViewModel : IDisposable
{
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

        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);
        TimeSpan curFrame = Scene.CurrentFrame;
        TimeSpan duration = Scene.Duration;

        using (var timer = new PeriodicTimer(tick))
        {
            DateTime dateTime = DateTime.UtcNow;
            while (await timer.WaitForNextTickAsync() &&
                curFrame <= duration &&
                IsPlaying.Value)
            {
                curFrame += tick;
                Render(renderer, curFrame);
            }
        }

        renderer.RenderInvalidated += Renderer_RenderInvalidated;
    }

    public void Pause()
    {
        IsPlaying.Value = false;
    }

    private void Render(IRenderer renderer, TimeSpan timeSpan)
    {
        if (renderer.IsRendering && !_isEnabled.Value)
            return;

        renderer.Dispatcher.Invoke(() =>
        {
            IRenderer.RenderResult result = renderer.Render(timeSpan);
            UpdateImage(result.Bitmap);
            result.Bitmap.Dispose();

            Scene.CurrentFrame = timeSpan;
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
        UpdateImage(e.Bitmap);
    }

    public void Dispose()
    {
        _disposable0.Dispose();
        _disposable1.Dispose();
    }
}
