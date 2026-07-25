using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.Styling;

namespace Beutl.HeadlessUITests;

// Locks the theme-extension split: the design colors ship as the default first-party theme extension
// (an avares override dictionary), while the built-in "Dark" stays registered as a selectable
// "Classic" theme.
[TestFixture]
public class DarkBorderThemeExtensionTests
{
    [AvaloniaTest]
    public void Descriptor_HasNonReservedId_ResourceUri_AndDarkVariant()
    {
        ThemeDescriptor descriptor = DarkBorderThemeExtension.Instance.GetThemeDescriptor();

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Id, Is.EqualTo(DarkBorderThemeExtension.ThemeId));
            Assert.That(BuiltinThemeIds.IsReserved(descriptor.Id), Is.False,
                "a reserved id would be rewritten to the built-in by settings normalization");
            Assert.That(descriptor.ResourceUri, Is.Not.Null);
            Assert.That(descriptor.BaseVariant, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(descriptor.DisplayName, Is.Not.Empty);
            Assert.That(descriptor.AccentColor, Is.EqualTo(Color.FromRgb(0x25, 0x63, 0xEB)),
                "the design accent seeds SystemAccentColor* while no custom accent is configured");
        });
    }

    [AvaloniaTest]
    public void ResourceUri_LoadsAsResourceProvider()
    {
        ThemeDescriptor descriptor = DarkBorderThemeExtension.Instance.GetThemeDescriptor();

        object? loaded = AvaloniaXamlLoader.Load(descriptor.ResourceUri!, null);

        Assert.That(loaded, Is.InstanceOf<IResourceProvider>());
    }

    [AvaloniaTest]
    public void ViewConfigDefault_MatchesThemeId()
    {
        // ViewConfig cannot reference the app-layer extension, so its default is a literal; this test
        // is what keeps the two in sync.
        Assert.That(new ViewConfig().Theme, Is.EqualTo(DarkBorderThemeExtension.ThemeId));
    }

    // The default theme ships as an extension, and in production the pass that loads extensions runs on
    // a background thread — after the first apply. Unless Start registers it itself, the app renders
    // classic dark and flashes to the near-black design once the pass lands (#2134).
    [AvaloniaTest]
    public void Start_ResolvesTheDefaultTheme_WithoutTheExtensionPass()
    {
        ClearRegistry();
        FluentAvaloniaTheme theme = Application.Current!.Styles.OfType<FluentAvaloniaTheme>().Single();
        var config = new ViewConfig();
        var service = new ThemeService(theme, config);
        try
        {
            service.Start();

            ThemeDescriptor? resolved = ThemeRegistry.Resolve(config.Theme);
            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.Not.Null, "the configured default must resolve at the first apply");
                Assert.That(resolved!.Id, Is.EqualTo(DarkBorderThemeExtension.ThemeId));
                Assert.That(ThemeRegistry.GetOwner(resolved), Is.SameAs(DarkBorderThemeExtension.Instance),
                    "the extension has to own its theme, or unloading it could not revert the app");
            });
        }
        finally
        {
            // Dispose before running the queued first apply: a disposed service drops it, which keeps
            // the dark override out of this Application and out of the Light capture tests.
            service.Dispose();
            ClearRegistry();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTest]
    public void Registry_ContainsNewThemeAndRelabeledClassicDark()
    {
        ClearRegistry();
        FluentAvaloniaTheme theme = Application.Current!.Styles.OfType<FluentAvaloniaTheme>().Single();
        var config = new ViewConfig { Theme = BuiltinThemeIds.Light };
        var service = new ThemeService(theme, config);
        var extension = new DarkBorderThemeExtension();
        try
        {
            // Selecting Light keeps the dark override from merging into Application.Resources, so this
            // test verifies the registry without leaking styling into later tests.
            service.Start();
            extension.Load();
            Dispatcher.UIThread.RunJobs();

            IReadOnlyList<ThemeDescriptor> themes = ThemeRegistry.Enumerate();
            ThemeDescriptor? newTheme = themes.FirstOrDefault(t => t.Id == DarkBorderThemeExtension.ThemeId);
            ThemeDescriptor? classicDark = themes.FirstOrDefault(t => t.Id == BuiltinThemeIds.Dark);

            Assert.Multiple(() =>
            {
                Assert.That(newTheme, Is.Not.Null, "the border theme extension should be registered");
                Assert.That(newTheme!.ResourceUri, Is.Not.Null);
                Assert.That(classicDark, Is.Not.Null, "built-in dark must remain selectable");
                Assert.That(classicDark!.DisplayName, Is.EqualTo(SettingsStrings.DarkClassic),
                    "built-in dark is relabeled so it is distinct from the border theme's 'Dark'");
            });
        }
        finally
        {
            // Dispose before touching the registry: Unregister raises Changed synchronously, and a
            // live service would post a ResolveAndApply job that outlives this test's cleanup.
            service.Dispose();
            extension.Unload();
            ClearRegistry();
            Dispatcher.UIThread.RunJobs();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    private static void ClearRegistry()
    {
        foreach (ThemeDescriptor descriptor in ThemeRegistry.Enumerate())
        {
            ThemeRegistry.Unregister(descriptor);
        }
    }
}
