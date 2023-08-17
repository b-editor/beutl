using System.Text.Json.Nodes;

using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class SceneSettingsTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposable = new();
    private Scene _scene;

    public SceneSettingsTabViewModel(EditViewModel editViewModel)
    {
        _scene = editViewModel.Scene;
        Width = _scene.GetObservable(Scene.WidthProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposable);
        Height = _scene.GetObservable(Scene.WidthProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposable);
        Duration = _scene.GetObservable(Scene.DurationProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposable);

        Width.SetValidateNotifyError(ValidateSize);
        Height.SetValidateNotifyError(ValidateSize);
        Duration.SetValidateNotifyError(time =>
        {
            if (time <= TimeSpan.Zero)
            {
                return Message.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });

        CanApply = Width.CombineLatest(Height, Duration).Select(t =>
            {
                int width = t.First;
                int height = t.Second;

                TimeSpan time = t.Third;
                return width > 0 &&
                    height > 0 &&
                    time > TimeSpan.Zero;
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposable);

        Apply = new ReactiveCommand(CanApply)
            .WithSubscribe(() =>
            {
                _scene.Initialize(Width.Value, Height.Value);
                _scene.Duration = Duration.Value;
            })
            .DisposeWith(_disposable);

        Revert = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                Width.Value = _scene.Width;
                Height.Value = _scene.Height;
                Duration.Value = _scene.Duration;
            })
            .DisposeWith(_disposable);
    }

    public ReactiveCommand Apply { get; }

    public ReactiveCommand Revert { get; }

    public ReactiveProperty<int> Width { get; }

    public ReactiveProperty<int> Height { get; }

    public ReactiveProperty<TimeSpan> Duration { get; }

    public ReadOnlyReactivePropertySlim<bool> CanApply { get; }

    public ToolTabExtension Extension => SceneSettingsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.SceneSettings;

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    private string? ValidateSize(int observable)
    {
        return observable <= 0 ? Message.ValueLessThanOrEqualToZero : null;
    }

    public void Dispose()
    {
        _disposable.Dispose();
        _scene = null!;
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }
}
