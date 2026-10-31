using Avalonia.Data.Converters;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;

namespace Beutl.Editor.Components.Converters;

public static class EnumIndexConverters
{
    public static readonly IValueConverter SpeakerLayouts = new EnumIndexConverter<SpeakerLayout>(SpeakerLayout.Stereo);

    public static readonly IValueConverter ScopeTypes = new EnumIndexConverter<ColorScopeType>(ColorScopeType.Waveform);

    public static readonly IValueConverter WaveformModes = new EnumIndexConverter<WaveformMode>(WaveformMode.Luma);

    public static readonly IValueConverter HistogramModes = new EnumIndexConverter<HistogramMode>(HistogramMode.Overlay);

    public static readonly IValueConverter ScopeColorSpaces = new EnumIndexConverter<ScopeColorSpace>(ScopeColorSpace.Gamma);

    public static readonly IValueConverter AudioVisualizerModes =
        new EnumIndexConverter<AudioVisualizerMode>(AudioVisualizerMode.Waveform);
}
