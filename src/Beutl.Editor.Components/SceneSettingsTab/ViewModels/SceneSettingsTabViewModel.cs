using System.Text.Json.Nodes;

using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Services;

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
        IObservable<Media.PixelSize> frameSize = _scene.FrameSize.GetObservable();
        Width = frameSize.Select(v => v.Width)
            .ToReactiveProperty()
            .DisposeWith(_disposable);
        Height = frameSize.Select(v => v.Height)
            .ToReactiveProperty()
            .DisposeWith(_disposable);
        StartInput = _scene.Start.GetObservable()
            .Select(TimeText.Format)
            .ToReactiveProperty()
            .DisposeWith(_disposable)!;
        DurationInput = _scene.Duration.GetObservable()
            .Select(TimeText.Format)
            .ToReactiveProperty()
            .DisposeWith(_disposable)!;
        LayerCount = _optionsProvider.Options.Select(x => x.MaxLayerCount)
            .ToReactiveProperty()
            .DisposeWith(_disposable);

        ChannelLayout = (_scene.FindHierarchicalParent<Project>()?.ChannelLayout.GetObservable()
                ?? Observable.Return(SpeakerLayout.Stereo))
            .ToReactiveProperty().DisposeWith(_disposable);

        Width.SetValidateNotifyError(ValidateSize);
        Height.SetValidateNotifyError(ValidateSize);
        LayerCount.SetValidateNotifyError(ValidateSize);

        StartInput.SetValidateNotifyError(StartValidator);
        DurationInput.SetValidateNotifyError(DurationValidator);

        CanApply = Width.CombineLatest(Height, StartInput, DurationInput, LayerCount)
            .Select(_ => TryReadSceneSettings(out var _, out var _, out var _) && LayerCount.Value > 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposable);

        Apply = new AsyncReactiveCommand(CanApply)
            .WithSubscribe(ApplyAsync)
            .DisposeWith(_disposable);

        Revert = new ReactiveCommand()
            .WithSubscribe(RevertInputs)
            .DisposeWith(_disposable);
    }

    private async Task ApplyAsync()
    {
        if (TryReadSceneSettings(out Media.PixelSize frameSize, out MediaTime start, out MediaTime duration))
        {
            // Pause playback before rebuilding the renderer to avoid UI freeze. Pause also returns a pause
            // still draining the last frame, which IsPlaying no longer shows.
            if ((frameSize != _scene.FrameSize.Value
                    || start != _scene.Start.Value
                    || duration != _scene.Duration.Value
                    || ChannelLayout.Value != (_scene.FindHierarchicalParent<Project>()?.ChannelLayout.Value ?? SpeakerLayout.Stereo))
                && _editorContext.GetService<IPreviewPlayer>() is { } player)
            {
                await player.Pause();

                if (!TryReadSceneSettings(out frameSize, out start, out duration))
                {
                    // Pausing yielded to the UI thread, so the user may have invalidated an
                    // input meanwhile. Warn instead of returning silently.
                    NotificationService.ShowWarning(
                        Strings.SceneSettings,
                        MessageStrings.SceneSettings_ApplyCanceledInputsInvalid);
                    return;
                }
            }

            _editorContext.GetRequiredService<ISceneSettingsService>().Apply(
                _scene,
                frameSize,
                start,
                duration,
                ChannelLayout.Value);

            _optionsProvider.Options.Value = _optionsProvider.Options.Value with
            {
                MaxLayerCount = LayerCount.Value
            };
        }
    }

    private void RevertInputs()
    {
        Width.Value = _scene.FrameSize.Value.Width;
        Height.Value = _scene.FrameSize.Value.Height;
        StartInput.Value = TimeText.Format(_scene.Start.Value);
        DurationInput.Value = TimeText.Format(_scene.Duration.Value);
        LayerCount.Value = _optionsProvider.Options.Value.MaxLayerCount;
        ChannelLayout.Value = _scene.FindHierarchicalParent<Project>()?.ChannelLayout.Value ?? SpeakerLayout.Stereo;
    }

    internal static string? DurationValidator(string str)
    {
        if (TimeText.TryParse(str, out MediaTime time))
        {
            if (time <= MediaTime.Zero)
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

    internal static string? StartValidator(string str)
    {
        if (TimeText.TryParse(str, out MediaTime time))
        {
            if (time < MediaTime.Zero)
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

    public ReactiveProperty<SpeakerLayout> ChannelLayout { get; }

    public ReadOnlyReactivePropertySlim<bool> CanApply { get; }

    public ToolTabExtension Extension => SceneSettingsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.SceneSettings);

    private string? ValidateSize(int observable)
    {
        return observable <= 0 ? MessageStrings.ValueLessThanOrEqualToZero : null;
    }

    private bool TryReadSceneSettings(out Media.PixelSize frameSize, out MediaTime start, out MediaTime duration)
    {
        frameSize = new Media.PixelSize(Width.Value, Height.Value);
        Rational rate = SceneTimeRangeService.GetFrameRate(_scene);
        bool hasStart = TimeText.TryParse(StartInput.Value, _scene.Start.Value, rate, out start);
        bool hasDuration = TimeText.TryParse(DurationInput.Value, _scene.Duration.Value, rate, out duration);
        return Width.Value > 0
            && Height.Value > 0
            && hasStart && start >= MediaTime.Zero
            && hasDuration && duration > MediaTime.Zero;
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
