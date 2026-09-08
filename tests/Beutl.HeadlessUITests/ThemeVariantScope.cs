using Avalonia;
using Avalonia.Styling;

namespace Beutl.HeadlessUITests;

// PerAssembly isolation (see TestAppBuilder) shares one Application across the whole run, so a test
// that leaves RequestedThemeVariant flipped picks the theme for every test after it.
internal sealed class ThemeVariantScope : IDisposable
{
    private readonly ThemeVariant? _previous;

    private ThemeVariantScope(ThemeVariant? previous) => _previous = previous;

    public static ThemeVariantScope Use(ThemeVariant variant)
    {
        var scope = new ThemeVariantScope(Application.Current!.RequestedThemeVariant);
        Application.Current.RequestedThemeVariant = variant;
        return scope;
    }

    public void Dispose() => Application.Current!.RequestedThemeVariant = _previous;
}
