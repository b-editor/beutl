using BeUtl.ProjectSystem;

using Reactive.Bindings;

namespace BeUtl.ViewModels;

public sealed class PlayerViewModel
{
    public PlayerViewModel(Scene scene)
    {
        Scene = scene;
        PlayPause.Subscribe(() =>
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

        Next.Subscribe(() =>
        {
            int rate = Project.GetFrameRate();
            if (rate <= 0)
            {
                rate = 30;
            }

            Scene.CurrentFrame += TimeSpan.FromSeconds(1d / rate);
        });

        Previous.Subscribe(() =>
        {
            int rate = Project.GetFrameRate();
            if (rate <= 0)
            {
                rate = 30;
            }

            Scene.CurrentFrame -= TimeSpan.FromSeconds(1d / rate);
        });

        Start.Subscribe(() => Scene.CurrentFrame = TimeSpan.Zero);

        End.Subscribe(() => Scene.CurrentFrame = Scene.Duration);
    }

    public Scene Scene { get; }

    public Project Project => Scene.FindRequiredLogicalParent<Project>();

    public ReactivePropertySlim<bool> IsPlaying { get; } = new();

    public ReactiveCommand PlayPause { get; } = new();

    public ReactiveCommand Next { get; } = new();

    public ReactiveCommand Previous { get; } = new();

    public ReactiveCommand Start { get; } = new();

    public ReactiveCommand End { get; } = new();

    public async void Play()
    {
        IsPlaying.Value = true;
        int rate = Project.GetFrameRate();
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
}
