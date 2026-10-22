using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace Beutl.Controls.Styling;

// High contrast inherits the light theme, which takes TextOnAccentColors.axaml; its keys go back to
// FluentAvalonia here, whose high contrast resources use other brushes for them.
internal sealed class HighContrastTextOnAccentResources : ResourceProvider, IThemeVariantProvider
{
    private static readonly Uri s_source = new("avares://Beutl.Controls/Styling/TextOnAccentColors.axaml");
    private HashSet<object>? _keys;

    public ThemeVariant? Key { get; set; }

    public override bool HasResources => true;

    public override bool TryGetResource(object key, ThemeVariant? theme, out object? value)
    {
        _keys ??= [.. ((IResourceDictionary)AvaloniaXamlLoader.Load(s_source)).Keys];
        if (_keys.Contains(key)
            && Application.Current?.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault() is { } fluent)
        {
            return fluent.TryGetResource(key, theme, out value);
        }

        value = null;
        return false;
    }
}
