using Avalonia.Data.Converters;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;

namespace Beutl.Editor.Components.Converters;

public static class EnumIndexConverters
{
    public static readonly IValueConverter SpeakerLayouts = new EnumIndexConverter<SpeakerLayout>(SpeakerLayout.Stereo);

    // The items of a ComboBox whose index goes through SpeakerLayouts, so in the order of the enum.
    public static string[] SpeakerLayoutNames =>
    [
        Strings.SpeakerLayout_Mono,
        Strings.SpeakerLayout_Stereo,
        Strings.SpeakerLayout_Surround51,
        Strings.SpeakerLayout_Surround71
    ];

    public static readonly IValueConverter ScopeTypes = new EnumIndexConverter<ColorScopeType>(ColorScopeType.Waveform);

    public static readonly IValueConverter WaveformModes = new EnumIndexConverter<WaveformMode>(WaveformMode.Luma);

    public static readonly IValueConverter HistogramModes = new EnumIndexConverter<HistogramMode>(HistogramMode.Overlay);

    public static readonly IValueConverter ScopeColorSpaces = new EnumIndexConverter<ScopeColorSpace>(ScopeColorSpace.Gamma);

    public static readonly IValueConverter AudioVisualizerModes =
        new EnumIndexConverter<AudioVisualizerMode>(AudioVisualizerMode.Waveform);
}
