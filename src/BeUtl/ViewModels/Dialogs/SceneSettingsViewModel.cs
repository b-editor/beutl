using Avalonia;

using BeUtl.ProjectSystem;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Dialogs;

public sealed class SceneSettingsViewModel
{
    private readonly Scene _scene;

    public SceneSettingsViewModel(Scene scene)
    {
        _scene = scene;
        Size = new ReactiveProperty<PixelSize>(new PixelSize(scene.Width, scene.Height));
        Duration = new ReactiveProperty<TimeSpan>(scene.Duration);
        Size.SetValidateNotifyError(s =>
        {
            if (s.Width <= 0 || s.Height <= 0)
            {
                return S.Warning.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });
        Duration.SetValidateNotifyError(time =>
        {
            if (time <= TimeSpan.Zero)
            {
                return S.Warning.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });

        CanApply = Size.CombineLatest(Duration).Select(t =>
        {
            PixelSize size = t.First;
            TimeSpan time = t.Second;
            return size.Width > 0 &&
                size.Height > 0 &&
                time > TimeSpan.Zero;
        }).ToReadOnlyReactivePropertySlim();

        Apply = new ReactiveCommand(CanApply);
        Apply.Subscribe(() =>
        {
            _scene.Initialize(Size.Value.Width, Size.Value.Height);
            _scene.Duration = Duration.Value;
        });
    }

    public ReactiveCommand Apply { get; }

    public ReactiveProperty<PixelSize> Size { get; }

    public ReactiveProperty<TimeSpan> Duration { get; }

    public ReadOnlyReactivePropertySlim<bool> CanApply { get; }
}
