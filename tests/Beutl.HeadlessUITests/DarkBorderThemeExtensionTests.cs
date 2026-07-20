using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml;
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

    [AvaloniaTest]
    public void Registry_ContainsNewThemeAndRelabeledClassicDark()
    {
        ClearRegistry();
        FluentAvaloniaTheme theme = Application.Current!.Styles.OfType<FluentAvaloniaTheme>().Single();
        var config = new ViewConfig { Theme = BuiltinThemeIds.Light };
        using var service = new ThemeService(theme, config);
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
                Assert.That(newTheme, Is.Not.Null, "the default theme extension should be registered");
                Assert.That(newTheme!.ResourceUri, Is.Not.Null);
                Assert.That(classicDark, Is.Not.Null, "built-in dark must remain selectable");
                Assert.That(classicDark!.DisplayName, Is.EqualTo(SettingsStrings.DarkClassic),
                    "built-in dark is relabeled so it is distinct from the default theme's 'Dark'");
            });
        }
        finally
        {
            extension.Unload();
            ClearRegistry();
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
