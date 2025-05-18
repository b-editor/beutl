using System.Text.Json.Nodes;

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
        IObservable<Media.PixelSize> frameSize = _scene.GetObservable(Scene.FrameSizeProperty);
        Width = frameSize.Select(v => v.Width)
            .ToReactiveProperty()
            .DisposeWith(_disposable);
        Height = frameSize.Select(v => v.Height)
            .ToReactiveProperty()
            .DisposeWith(_disposable);
        StartInput = _scene.GetObservable(Scene.StartProperty)
            .Select(v => v.ToString())
            .ToReactiveProperty()
            .DisposeWith(_disposable)!;
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

        StartInput.SetValidateNotifyError(TimeSpanValidator);
        DurationInput.SetValidateNotifyError(TimeSpanValidator);

        CanApply = Width.CombineLatest(Height, StartInput, DurationInput, LayerCount).Select(t =>
            {
                int width = t.First;
                int height = t.Second;

                string startTime = t.Third;
                string durationTime = t.Fourth;
                return width > 0 &&
                    height > 0 &&
                    TimeSpan.TryParse(startTime, out TimeSpan ts1) && ts1 > TimeSpan.Zero &&
                    TimeSpan.TryParse(durationTime, out TimeSpan ts2) && ts2 > TimeSpan.Zero &&
                    t.Fifth > 0;
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposable);

        Apply = new ReactiveCommand(CanApply)
            .WithSubscribe(() =>
            {
                if (TimeSpan.TryParse(StartInput.Value, out TimeSpan start)
                    &&TimeSpan.TryParse(DurationInput.Value, out TimeSpan duration))
                {
                    if (Width.Value != _scene.FrameSize.Width
                        || Height.Value != _scene.FrameSize.Height
                        || start != _scene.Start
                        || duration != _scene.Duration)
                    {
                        CommandRecorder recorder = _editViewModel.CommandRecorder;
                        RecordableCommands.Edit(_scene, Scene.FrameSizeProperty, new(Width.Value, Height.Value))
                            .Append(RecordableCommands.Edit(_scene, Scene.StartProperty, start))
                            .Append(RecordableCommands.Edit(_scene, Scene.DurationProperty, duration))
                            .WithStoables([_scene])
                            .DoAndRecord(recorder);
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
                Width.Value = _scene.FrameSize.Width;
                Height.Value = _scene.FrameSize.Height;
                StartInput.Value = _scene.Start.ToString();
                DurationInput.Value = _scene.Duration.ToString();
                LayerCount.Value = _editViewModel.Options.Value.MaxLayerCount;
            })
            .DisposeWith(_disposable);
        return;
    }

    private static string? TimeSpanValidator(string str)
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
    }

    public ReactiveCommand Apply { get; }

    public ReactiveCommand Revert { get; }

    public ReactiveProperty<int> Width { get; }

    public ReactiveProperty<int> Height { get; }

    public ReactiveProperty<string> StartInput { get; }

    public ReactiveProperty<string> DurationInput { get; }

    public ReactiveProperty<int> LayerCount { get; }

    public ReadOnlyReactivePropertySlim<bool> CanApply { get; }

    public ToolTabExtension Extension => SceneSettingsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.SceneSettings;

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.Right);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

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
}
