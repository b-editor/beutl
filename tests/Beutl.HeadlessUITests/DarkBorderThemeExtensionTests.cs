using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Controls.Styling;
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

    // Only this layer can compare the two: ViewConfig's default is the shared FirstPartyThemeIds id,
    // and the extension's descriptor is what registers under it.
    [AvaloniaTest]
    public void ViewConfigDefault_MatchesThemeId()
    {
        Assert.That(new ViewConfig().Theme, Is.EqualTo(DarkBorderThemeExtension.ThemeId));
    }

    // In production the extension pass runs on a background thread, after the first apply. Unless Start
    // registers the default theme itself, the app renders classic dark and flashes to it (#2134).
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

    // Start loads the default theme and the primitive-extension pass loads it again, on a background
    // thread. That is only harmless because the descriptor instance is stable: a fresh record would be
    // a new registration, which ThemeService cannot reference-skip and would re-apply as a theme change.
    [AvaloniaTest]
    public void RepeatLoad_RegistersTheSameDescriptor_AndAppliesNothing()
    {
        ClearRegistry();
        FluentAvaloniaTheme theme = Application.Current!.Styles.OfType<FluentAvaloniaTheme>().Single();
        IResourceProvider[] mergedOnEntry = [.. Application.Current.Resources.MergedDictionaries];
        ThemeVariant? variantOnEntry = Application.Current.RequestedThemeVariant;
        var config = new ViewConfig();
        var service = new ThemeService(theme, config);
        try
        {
            service.Start();
            Dispatcher.UIThread.RunJobs();
            ThemeDescriptor? applied = ThemeRegistry.Resolve(config.Theme);
            IResourceProvider[] mergedAfterFirstLoad = [.. Application.Current.Resources.MergedDictionaries];

            DarkBorderThemeExtension.Instance.Load();
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.Not.Null, "precondition: the default theme was registered and resolved");
                Assert.That(ThemeRegistry.Resolve(config.Theme), Is.SameAs(applied),
                    "a repeat Load must leave the registered descriptor instance untouched");
                Assert.That(Application.Current.Resources.MergedDictionaries, Is.EqualTo(mergedAfterFirstLoad),
                    "a repeat Load must not swap the applied theme's resources");
            });
        }
        finally
        {
            service.Dispose();
            DarkBorderThemeExtension.Instance.Unload();
            ClearRegistry();
            Dispatcher.UIThread.RunJobs();

            // This test lets the apply run, so it owns what the apply left on the Application: the
            // merged override dictionary and the accent, both process-global here.
            IList<IResourceProvider> merged = Application.Current.Resources.MergedDictionaries;
            foreach (IResourceProvider added in merged.Except(mergedOnEntry).ToArray())
            {
                merged.Remove(added);
            }

            theme.CustomAccentColor = null;
            AccentResolution.ApplyTextOnAccent(Application.Current.Resources, null);
            Application.Current.RequestedThemeVariant = variantOnEntry;
        }
    }

    [AvaloniaTest]
    public void Registry_ContainsNewThemeAndRelabeledClassicDark()
    {
        ClearRegistry();
        FluentAvaloniaTheme theme = Application.Current!.Styles.OfType<FluentAvaloniaTheme>().Single();
        ThemeVariant? variantOnEntry = Application.Current.RequestedThemeVariant;
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
            Application.Current!.RequestedThemeVariant = variantOnEntry;
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
