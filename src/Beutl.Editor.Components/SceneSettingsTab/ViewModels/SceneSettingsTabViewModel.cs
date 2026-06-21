using System.Text.Json.Nodes;

using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.SceneSettingsTab.ViewModels;

public sealed class SceneSettingsTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposable = [];
    private IEditorContext _editorContext;
    private Scene _scene;
    private ITimelineOptionsProvider _optionsProvider;

    public SceneSettingsTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        _scene = editorContext.GetService<Scene>()!;
        _optionsProvider = editorContext.GetService<ITimelineOptionsProvider>()!;
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
        LayerCount = _optionsProvider.Options.Select(x => x.MaxLayerCount)
            .ToReactiveProperty()
            .DisposeWith(_disposable);

        Width.SetValidateNotifyError(ValidateSize);
        Height.SetValidateNotifyError(ValidateSize);
        LayerCount.SetValidateNotifyError(ValidateSize);

        StartInput.SetValidateNotifyError(StartValidator);
        DurationInput.SetValidateNotifyError(DurationValidator);

        CanApply = Width.CombineLatest(Height, StartInput, DurationInput, LayerCount).Select(t =>
            {
                int width = t.First;
                int height = t.Second;

                string startTime = t.Third;
                string durationTime = t.Fourth;
                return width > 0 &&
                    height > 0 &&
                    TimeSpan.TryParse(startTime, out TimeSpan ts1) && ts1 >= TimeSpan.Zero &&
                    TimeSpan.TryParse(durationTime, out TimeSpan ts2) && ts2 > TimeSpan.Zero &&
                    t.Fifth > 0;
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposable);

        Apply = new AsyncReactiveCommand(CanApply)
            .WithSubscribe(async () =>
            {
                if (TryReadSceneSettings(out Media.PixelSize frameSize, out TimeSpan start, out TimeSpan duration))
                {
                    // Pause playback before rebuilding the renderer to avoid UI freeze.
                    if ((frameSize != _scene.FrameSize
                            || start != _scene.Start
                            || duration != _scene.Duration)
                        && _editorContext.GetService<IPreviewPlayer>() is { IsPlaying.Value: true } player)
                    {
                        await player.Pause();

                        if (!TryReadSceneSettings(out frameSize, out start, out duration))
                        {
                            return;
                        }
                    }

                    _editorContext.GetRequiredService<ISceneSettingsService>().Apply(
                        _scene,
                        frameSize,
                        start,
                        duration);

                    _optionsProvider.Options.Value = _optionsProvider.Options.Value with
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
                LayerCount.Value = _optionsProvider.Options.Value.MaxLayerCount;
            })
            .DisposeWith(_disposable);
    }

    private static string? DurationValidator(string str)
    {
        if (TimeSpan.TryParse(str, out TimeSpan time))
        {
            if (time <= TimeSpan.Zero)
            {
                return MessageStrings.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        }
        else
        {
            return MessageStrings.InvalidString;
        }
    }

    private static string? StartValidator(string str)
    {
        if (TimeSpan.TryParse(str, out TimeSpan time))
        {
            if (time < TimeSpan.Zero)
            {
                return MessageStrings.ValueLessThanZero;
            }
            else
            {
                return null;
            }
        }
        else
        {
            return MessageStrings.InvalidString;
        }
    }

    public AsyncReactiveCommand Apply { get; }

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

    private string? ValidateSize(int observable)
    {
        return observable <= 0 ? MessageStrings.ValueLessThanOrEqualToZero : null;
    }

    private bool TryReadSceneSettings(out Media.PixelSize frameSize, out TimeSpan start, out TimeSpan duration)
    {
        frameSize = new Media.PixelSize(Width.Value, Height.Value);
        bool hasStart = TimeSpan.TryParse(StartInput.Value, out start);
        bool hasDuration = TimeSpan.TryParse(DurationInput.Value, out duration);
        return hasStart && hasDuration;
    }

    public void Dispose()
    {
        _disposable.Dispose();
        _editorContext = null!;
        _scene = null!;
        _optionsProvider = null!;
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
