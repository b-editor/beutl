using Beutl.Configuration;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class EditorSettingsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public EditorSettingsPageViewModel(GlobalConfiguration configuration)
    {
        EditorConfig config = configuration.EditorConfig;
        AutoAdjustSceneDuration = SettingsPageBinding.Bind(config.AutoAdjustSceneDuration, _disposables);
        SwapTimelineScrollDirection = SettingsPageBinding.Bind(config.SwapTimelineScrollDirection, _disposables);
        SeekOnTimelineClick = SettingsPageBinding.Bind(config.SeekOnTimelineClick, _disposables);
        ClampResizeToOriginalLength = SettingsPageBinding.Bind(config.ClampResizeToOriginalLength, _disposables);
        ShowTimelineThumbnailsByDefault = SettingsPageBinding.Bind(config.ShowTimelineThumbnailsByDefault, _disposables);
        EnablePointerLockInProperty = SettingsPageBinding.Bind(config.EnablePointerLockInProperty, _disposables);

        TimelineLayerSize = SettingsPageBinding.Bind(
            config.TimelineLayerSize.GetObservable().Select(EditorConfig.GetTimelineLayerSizeIndex),
            config.SetTimelineLayerSizeFromIndex,
            _disposables);

        TimelineAutoScrollMode = SettingsPageBinding.Bind(
            config.TimelineAutoScrollMode.GetObservable().Select(v => (int)v),
            index => config.TimelineAutoScrollMode.Value = (TimelineAutoScrollMode)index,
            _disposables);

        TimeDisplayFormat = SettingsPageBinding.Bind(
            config.TimeDisplayFormat.GetObservable().Select(v => (int)v),
            index =>
            {
                // The -1 of a transient no-selection is no format.
                if (Enum.IsDefined((TimeDisplayFormat)index))
                    config.TimeDisplayFormat.Value = (TimeDisplayFormat)index;
            },
            _disposables);
    }

    public ReactiveProperty<bool> AutoAdjustSceneDuration { get; }

    public ReactiveProperty<bool> SwapTimelineScrollDirection { get; }

    public ReactiveProperty<bool> SeekOnTimelineClick { get; }

    public ReactiveProperty<int> TimelineAutoScrollMode { get; }

    public ReactiveProperty<int> TimeDisplayFormat { get; }

    public ReactiveProperty<bool> ClampResizeToOriginalLength { get; }

    public ReactiveProperty<bool> ShowTimelineThumbnailsByDefault { get; }

    public ReactiveProperty<int> TimelineLayerSize { get; }

    public ReactiveProperty<bool> EnablePointerLockInProperty { get; }

    public void Dispose() => _disposables.Dispose();
}
