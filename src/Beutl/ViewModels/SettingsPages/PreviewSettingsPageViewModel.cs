using Beutl.Configuration;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Graphics.Backend.Vulkan;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class PreviewSettingsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public PreviewSettingsPageViewModel(EditorConfig config)
    {
        ToneMappingMode = SettingsPageBinding.Bind(
            config.ToneMappingMode.GetObservable().Select(EditorConfig.GetToneMappingModeIndex),
            config.SetToneMappingModeFromIndex,
            _disposables);

        ToneMappingExposure = SettingsPageBinding.Bind(
            config.ToneMappingExposure.GetObservable().Select(v => (double)v),
            value => config.ToneMappingExposure.Value = (float)value,
            _disposables);

        UseHdrPreview = SettingsPageBinding.Bind(config.UseHdrPreview, _disposables);

        DisplayPeakLuminance = SettingsPageBinding.Bind(
            config.DisplayPeakLuminance.GetObservable().Select(v => (double)v),
            value => config.DisplayPeakLuminance.Value = (float)value,
            _disposables);

        DisplayPeakLuminanceDescription = new ReactiveProperty<string>(DescribeDisplayPeakLuminance(null))
            .DisposeWith(_disposables);

        PreviewSourceMode = SettingsPageBinding.Bind(
            config.PreviewSourceMode.GetObservable().Select(v => (int)v),
            config.SetPreviewSourceModeFromIndex,
            _disposables);
        PreviewGuides = new PreviewGuideSettingsViewModel(config).DisposeWith(_disposables);
    }

    public ReactiveProperty<int> ToneMappingMode { get; }

    public ReactiveProperty<double> ToneMappingExposure { get; }

    public ReactiveProperty<bool> UseHdrPreview { get; }

    public ReactiveProperty<double> DisplayPeakLuminance { get; }

    public ReactiveProperty<string> DisplayPeakLuminanceDescription { get; }

    public ReactiveProperty<int> PreviewSourceMode { get; }

    public PreviewGuideSettingsViewModel PreviewGuides { get; }

    // Windows only: Core Animation fits macOS's preview to the display itself, and elsewhere its white is not known.
    public bool IsDisplayPeakLuminanceSupported { get; internal init; } = OperatingSystem.IsWindows();

    // Shows what the display the settings are on reports, for judging whether its peak needs overriding.
    internal void ShowReportedDisplay(HdrDisplayReport? report)
    {
        DisplayPeakLuminanceDescription.Value = DescribeDisplayPeakLuminance(report);
    }

    private static string DescribeDisplayPeakLuminance(HdrDisplayReport? report)
    {
        string reported = report is { } r
            ? string.Format(SettingsStrings.DisplayPeakLuminance_Reported, r.PeakNits, r.SdrWhiteNits)
            : SettingsStrings.DisplayPeakLuminance_NotHdr;
        return $"{SettingsStrings.DisplayPeakLuminance_Description}\n{reported}";
    }

    public void Dispose() => _disposables.Dispose();
}
