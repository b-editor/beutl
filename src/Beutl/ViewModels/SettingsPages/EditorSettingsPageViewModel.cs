using Beutl.Configuration;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class EditorSettingsPageViewModel
{
    private readonly EditorConfig _editorConfig;

    public EditorSettingsPageViewModel()
    {
        _editorConfig = GlobalConfiguration.Instance.EditorConfig;

        AutoAdjustSceneDuration = _editorConfig.GetObservable(EditorConfig.AutoAdjustSceneDurationProperty).ToReactiveProperty();
        AutoAdjustSceneDuration.Subscribe(b => _editorConfig.AutoAdjustSceneDuration = b);
    }

    public ReactiveProperty<bool> AutoAdjustSceneDuration { get; }
}
