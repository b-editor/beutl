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

        FrameRateInput = _scene.FrameRate.GetObservable()
            .Select(FrameRateText.Format)
            .ToReactiveProperty()
            .DisposeWith(_disposable)!;

        SampleRate = _scene.SampleRate.GetObservable()
            .ToReactiveProperty()
            .DisposeWith(_disposable);

        ChannelLayout = _scene.ChannelLayout.GetObservable()
            .ToReactiveProperty()
            .DisposeWith(_disposable);

        MasterVolume = _scene.MasterVolume.GetObservable()
            .ToReactiveProperty()
            .DisposeWith(_disposable);

        Width.SetValidateNotifyError(ValidateSize);
        Height.SetValidateNotifyError(ValidateSize);
        LayerCount.SetValidateNotifyError(ValidateSize);
        FrameRateInput.SetValidateNotifyError(FrameRateText.Validate);
        SampleRate.SetValidateNotifyError(ValidateSize);
        MasterVolume.SetValidateNotifyError(volume => volume >= 0 ? null : MessageStrings.ValueLessThanZero);

        StartInput.SetValidateNotifyError(StartValidator);
        DurationInput.SetValidateNotifyError(DurationValidator);

        CanApply = Width.CombineLatest(Height, StartInput, DurationInput, LayerCount, MasterVolume)
            .CombineLatest(FrameRateInput, SampleRate)
            .Select(_ => TryReadSceneSettings(out var _, out var _, out var _, out var _) && LayerCount.Value > 0 && MasterVolume.Value >= 0)
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
        if (TryReadSceneSettings(out Media.PixelSize frameSize, out MediaTime start, out MediaTime duration, out Rational frameRate))
        {
            // Pause playback before rebuilding the renderer to avoid UI freeze. Pause also returns a pause
            // still draining the last frame, which IsPlaying no longer shows.
            if ((frameSize != _scene.FrameSize.Value
                    || start != _scene.Start.Value
                    || duration != _scene.Duration.Value
                    || frameRate != _scene.FrameRate.Value
                    || SampleRate.Value != _scene.SampleRate.Value
                    || ChannelLayout.Value != _scene.ChannelLayout.Value)
                && _editorContext.GetService<IPreviewPlayer>() is { } player)
            {
                await player.Pause();

                if (!TryReadSceneSettings(out frameSize, out start, out duration, out frameRate) || MasterVolume.Value < 0)
                {
                    // Pausing yielded to the UI thread, so the user may have invalidated an
                    // input meanwhile. Warn instead of returning silently.
                    _editorContext.GetRequiredService<INotificationService>().ShowWarning(
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
                frameRate,
                SampleRate.Value,
                ChannelLayout.Value,
                MasterVolume.Value);

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
        FrameRateInput.Value = FrameRateText.Format(_scene.FrameRate.Value);
        SampleRate.Value = _scene.SampleRate.Value;
        ChannelLayout.Value = _scene.ChannelLayout.Value;
        MasterVolume.Value = _scene.MasterVolume.Value;
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

    public ReactiveProperty<string> FrameRateInput { get; }

    public IReadOnlyList<string> FrameRates => FrameRateText.Presets;

    public ReactiveProperty<int> SampleRate { get; }

    public ReactiveProperty<SpeakerLayout> ChannelLayout { get; }

    public ReactiveProperty<float> MasterVolume { get; }

    public ReadOnlyReactivePropertySlim<bool> CanApply { get; }

    public ToolTabExtension Extension => SceneSettingsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.SceneSettings);

    private string? ValidateSize(int observable)
    {
        return observable <= 0 ? MessageStrings.ValueLessThanOrEqualToZero : null;
    }

    private bool TryReadSceneSettings(
        out Media.PixelSize frameSize, out MediaTime start, out MediaTime duration, out Rational frameRate)
    {
        frameSize = new Media.PixelSize(Width.Value, Height.Value);
        Rational currentRate = SceneTimeRangeService.GetFrameRate(_scene);
        bool hasStart = TimeText.TryParse(StartInput.Value, _scene.Start.Value, currentRate, out start);
        bool hasDuration = TimeText.TryParse(DurationInput.Value, _scene.Duration.Value, currentRate, out duration);
        Rational? parsedRate = FrameRateText.Parse(FrameRateInput.Value);
        frameRate = parsedRate ?? default;
        return Width.Value > 0
            && Height.Value > 0
            && hasStart && start >= MediaTime.Zero
            && hasDuration && duration > MediaTime.Zero
            && parsedRate is { Numerator: > 0, Denominator: > 0 }
            && SampleRate.Value > 0;
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
