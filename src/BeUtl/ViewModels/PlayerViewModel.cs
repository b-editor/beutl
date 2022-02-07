using BeUtl.ProjectSystem;

using Reactive.Bindings;

namespace BeUtl.ViewModels;

public sealed class PlayerViewModel
{
    public PlayerViewModel(Scene scene)
    {
        Scene = scene;
    }

    public Scene Scene { get; }

    public Project Project => Scene.FindRequiredLogicalParent<Project>();

    public ReactivePropertySlim<bool> IsPlaying { get; } = new();

    public async void Play()
    {
        IsPlaying.Value = true;
        int rate = Project.FrameRate;
        if (rate >= 0)
        {
            rate = 30;
        }

        TimeSpan tick = TimeSpan.FromSeconds(1d / rate);
        using (var timer = new PeriodicTimer(tick))
        {
            while (await timer.WaitForNextTickAsync() &&
                Scene.CurrentFrame <= Scene.Duration &&
                IsPlaying.Value)
            {
                Scene.CurrentFrame += tick;
            }
        }
    }

    public void Pause()
    {
        IsPlaying.Value = false;
    }

    public void Next()
    {
        int rate = Project.FrameRate;
        if (rate <= 0)
        {
            rate = 30;
        }

        Scene.CurrentFrame += TimeSpan.FromSeconds(1d / rate);
    }

    public void Previous()
    {
        int rate = Project.FrameRate;
        if (rate <= 0)
        {
            rate = 30;
        }

        Scene.CurrentFrame -= TimeSpan.FromSeconds(1d / rate);
    }

    public void Start()
    {
        Scene.CurrentFrame = TimeSpan.Zero;
    }

    public void End()
    {
        Scene.CurrentFrame = Scene.Duration;
    }
}
