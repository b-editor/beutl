using Avalonia.Data.Converters;

namespace Beutl.Editor.Components.TimelineTab.Generative;

public static class TimelineAiPopupConverters
{
    /// <summary>Dims a model the account cannot run, without hiding it.</summary>
    public static readonly IValueConverter AvailableOpacity =
        new FuncValueConverter<bool, double>(available => available ? 1d : 0.5d);
}
