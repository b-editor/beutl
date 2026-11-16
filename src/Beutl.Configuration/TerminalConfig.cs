using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Beutl.Configuration;

public enum TerminalColorScheme
{
    FollowApplication,
    Dark,
    Light
}

public sealed partial class TerminalConfig : ConfigurationBase
{
    public const int MinFontSize = 8;
    public const int MaxFontSize = 72;
    public const int MaxScrollbackLines = 1_000_000;

    public TerminalConfig()
    {
        ScanProperties<TerminalConfig>();
    }

    public IProperty<TerminalColorScheme> ColorScheme { get; } = Property.CreateValue(TerminalColorScheme.FollowApplication);

    public IProperty<string> FontFamily { get; } = Property.CreateValue(string.Empty);

    /// <summary>The font size in device-independent pixels, matching Avalonia's FontSize.</summary>
    [Range(MinFontSize, MaxFontSize)]
    public IProperty<int> FontSize { get; } = Property.CreateValue(14);

    [Range(0, MaxScrollbackLines)]
    public IProperty<int> ScrollbackLines { get; } = Property.CreateValue(5000);

    public IProperty<string?> ShellPath { get; } = Property.CreateValue<string?>(null);

    // One argument per line, so paths and arguments containing spaces need no quoting.
    public IProperty<string?> ShellArguments { get; } = Property.CreateValue<string?>(null);

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
            OnChanged();
    }
}
