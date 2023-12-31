using System.Text.Json.Nodes;

using Avalonia;

using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class SceneSettingsTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposable = [];
    private EditViewModel _editViewModel;
    private Scene _scene;

    public SceneSettingsTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        _scene = editViewModel.Scene;
        Width = _scene.GetObservable(Scene.WidthProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposable);
        Height = _scene.GetObservable(Scene.HeightProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposable);
        DurationInput = _scene.GetObservable(Scene.DurationProperty)
            .Select(v => v.ToString())
            .ToReactiveProperty()
            .DisposeWith(_disposable)!;
        LayerCount = editViewModel.Options.Select(x => x.MaxLayerCount)
            .ToReactiveProperty()
            .DisposeWith(_disposable);

        Width.SetValidateNotifyError(ValidateSize);
        Height.SetValidateNotifyError(ValidateSize);
        LayerCount.SetValidateNotifyError(ValidateSize);
        DurationInput.SetValidateNotifyError(str =>
        {
            if (TimeSpan.TryParse(str, out TimeSpan time))
            {
                if (time <= TimeSpan.Zero)
                {
                    return Message.ValueLessThanOrEqualToZero;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return Message.InvalidString;
            }
        });

        CanApply = Width.CombineLatest(Height, DurationInput, LayerCount).Select(t =>
            {
                int width = t.First;
                int height = t.Second;

                string time = t.Third;
                return width > 0 &&
                    height > 0 &&
                    TimeSpan.TryParse(time, out TimeSpan ts) && ts > TimeSpan.Zero &&
                    t.Fourth > 0;
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposable);

        Apply = new ReactiveCommand(CanApply)
            .WithSubscribe(() =>
            {
                if (TimeSpan.TryParse(DurationInput.Value, out TimeSpan ts))
                {
                    if (Width.Value != _scene.Width
                        || Height.Value != _scene.Height
                        || ts != _scene.Duration)
                    {
                        new UpdateSceneSettingsCommand(new(Width.Value, Height.Value), ts, _scene)
                                        .DoAndRecord(CommandRecorder.Default);
                    }

                    _editViewModel.Options.Value = _editViewModel.Options.Value with
                    {
                        MaxLayerCount = LayerCount.Value
                    };
                }
            })
            .DisposeWith(_disposable);

        Revert = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                Width.Value = _scene.Width;
                Height.Value = _scene.Height;
                DurationInput.Value = _scene.Duration.ToString();
                LayerCount.Value = _editViewModel.Options.Value.MaxLayerCount;
            })
            .DisposeWith(_disposable);
    }

    public ReactiveCommand Apply { get; }

    public ReactiveCommand Revert { get; }

    public ReactiveProperty<int> Width { get; }

    public ReactiveProperty<int> Height { get; }

    public ReactiveProperty<string> DurationInput { get; }

    public ReactiveProperty<int> LayerCount { get; }

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
        _editViewModel = null!;
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

    private sealed class UpdateSceneSettingsCommand(PixelSize newSize, TimeSpan newDuration, Scene scene) : IRecordableCommand
    {
        private readonly PixelSize _oldSize = new(scene.Width, scene.Height);
        private readonly TimeSpan _oldDuration = scene.Duration;

        public void Do()
        {
            scene.Initialize(newSize.Width, newSize.Height);
            scene.Duration = newDuration;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            scene.Initialize(_oldSize.Width, _oldSize.Height);
            scene.Duration = _oldDuration;
        }
    }
}
