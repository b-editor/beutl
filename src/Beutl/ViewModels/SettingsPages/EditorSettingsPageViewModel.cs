using Beutl.Configuration;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class EditorSettingsPageViewModel
{
    private readonly ViewConfig _viewConfig;
    private readonly EditorConfig _editorConfig;

    public EditorSettingsPageViewModel()
    {
        _viewConfig = GlobalConfiguration.Instance.ViewConfig;
        _editorConfig = GlobalConfiguration.Instance.EditorConfig;

        AutoAdjustSceneDuration = _editorConfig.GetObservable(EditorConfig.AutoAdjustSceneDurationProperty).ToReactiveProperty();
        AutoAdjustSceneDuration.Subscribe(b => _editorConfig.AutoAdjustSceneDuration = b);

        EnablePointerLockInProperty = _editorConfig.GetObservable(EditorConfig.EnablePointerLockInPropertyProperty).ToReactiveProperty();
        EnablePointerLockInProperty.Subscribe(b => _editorConfig.EnablePointerLockInProperty = b);

        ShowExactBoundaries = _viewConfig.GetObservable(ViewConfig.ShowExactBoundariesProperty).ToReactiveProperty();
        ShowExactBoundaries.Subscribe(b => _viewConfig.ShowExactBoundaries = b);

        HidePrimaryProperties = _viewConfig.GetObservable(ViewConfig.HidePrimaryPropertiesProperty).ToReactiveProperty();
        HidePrimaryProperties.Subscribe(b => _viewConfig.HidePrimaryProperties = b);

        PrimaryProperties = _viewConfig.PrimaryProperties;

        RemovePrimaryProperty.Subscribe(v => PrimaryProperties.Remove(v));
        ResetPrimaryProperty.Subscribe(_ => _viewConfig.ResetPrimaryProperties());
    }

    public ReactiveProperty<bool> AutoAdjustSceneDuration { get; }
    
    public ReactiveProperty<bool> EnablePointerLockInProperty { get; }

    public ReactiveProperty<bool> ShowExactBoundaries { get; }

    public ReactiveProperty<bool> HidePrimaryProperties { get; }

    public CoreList<string> PrimaryProperties { get; }

    public ReactiveCommand<string> RemovePrimaryProperty { get; } = new();

    public ReactiveCommand ResetPrimaryProperty { get; } = new();
}
