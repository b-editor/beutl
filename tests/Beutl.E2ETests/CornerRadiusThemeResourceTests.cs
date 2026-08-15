using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests;

// FluentAvalonia ships ControlCornerRadius=4 and OverlayCornerRadius=8; Beutl.Controls' Styles.axaml
// replaces both, and that only takes effect because Application.Styles is searched last-entry-first.
// Reordering the StyleIncludes or dropping the merged dictionary would silently restore the stock
// rounding across the whole shell, so pin the resolved values rather than the file contents.
[TestFixture]
public class CornerRadiusThemeResourceTests
{
    [AvaloniaTest]
    public void ControlCornerRadius_IsBeutlsRounding()
    {
        bool found = Application.Current!.TryGetResource(
            "ControlCornerRadius", Application.Current.ActualThemeVariant, out object? value);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True, "ControlCornerRadius must resolve");
            Assert.That(value, Is.EqualTo(new CornerRadius(8)), "Beutl's value, not FluentAvalonia's 4");
        });
    }

    [AvaloniaTest]
    public void OverlayCornerRadius_IsBeutlsRounding()
    {
        bool found = Application.Current!.TryGetResource(
            "OverlayCornerRadius", Application.Current.ActualThemeVariant, out object? value);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True, "OverlayCornerRadius must resolve");
            Assert.That(value, Is.EqualTo(new CornerRadius(12)), "Beutl's value, not FluentAvalonia's 8");
        });
    }

    // FluentAvalonia's own control themes reach the key with DynamicResource, so they resolve against
    // the live application chain — this is what carries the override to most of the shell.
    [AvaloniaTest]
    public void AFluentAvaloniaControl_TakesTheOverride()
    {
        var button = new Button { Content = "ok" };
        using var host = new ControlHost(button);

        Assert.That(button.CornerRadius, Is.EqualTo(new CornerRadius(8)));
    }

    // Beutl's own dictionaries use StaticResource instead, which resolves through the parse-time parent
    // stack of Beutl.Controls' merged dictionaries — a separate path that has to find the override too.
    [AvaloniaTest]
    public void ABeutlControlTheme_TakesTheOverride()
    {
        var item = new OptionsDisplayItem { Header = "header" };
        using var host = new ControlHost(item);

        Assert.That(item.CornerRadius, Is.EqualTo(new CornerRadius(8)));
    }

    private sealed class ControlHost : IDisposable
    {
        private readonly Window _window;

        public ControlHost(Control content)
        {
            _window = new Window { Width = 320, Height = 200, Content = content };
            _window.Show();
            HeadlessTestHelpers.Settle();
        }

        public void Dispose()
        {
            _window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
