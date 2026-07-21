using System.Linq;
using Avalonia;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Services;
using FluentAvalonia.Styling;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ThemeServiceTests
{
    [AvaloniaTest]
    public void ApplyTheme_WhenResourceFailsToLoad_LeavesCurrentThemeIntact()
    {
        // A theme whose ResourceUri cannot be loaded must not be applied at all. Committing the
        // variant before the load means a bad extension release half-applies: the base variant
        // switches and the previous theme's resources are gone, but its brush overrides never merge.
        using var scope = new ThemeScope();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        scope.Service.Start();
        Dispatcher.UIThread.RunJobs();

        var broken = new ThemeDescriptor(
            "test.broken", "Broken", ThemeVariant.Light, new Uri("avares://NoSuchAssembly/Missing.axaml"));
        ThemeRegistry.Register(broken);

        scope.Config.Theme = "test.broken";
        Dispatcher.UIThread.RunJobs();

        Assert.That(Application.Current.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Dark));
    }

    [AvaloniaTest]
    public void StartWithABrokenSelectedTheme_FallsBackToDark()
    {
        // Extensions load before ThemeService.Start, so the selected theme can already be registered
        // — and broken — at the first apply. There is no current theme to keep and nothing
        // guarantees another trigger, so bailing out would leave the app unthemed.
        using var scope = new ThemeScope();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        var broken = new ThemeDescriptor(
            "test.brokenstartup", "Broken", ThemeVariant.Light, new Uri("avares://NoSuchAssembly/Missing.axaml"));
        ThemeRegistry.Register(broken);
        scope.Config.Theme = "test.brokenstartup";

        scope.Service.Start();
        Dispatcher.UIThread.RunJobs();

        Assert.That(Application.Current.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Dark));
    }

    [AvaloniaTest]
    public void FailedReplacementOfTheActiveTheme_FallsBackInsteadOfKeepingTheEvictedOne()
    {
        // A second extension takes over the applied id with a broken theme. Keeping the old
        // descriptor would leave an evicted theme active — and its owner would never be reverted,
        // because the stale Unregister at its Unload raises no Changed.
        using var scope = new ThemeScope();
        scope.Service.Start();
        Dispatcher.UIThread.RunJobs();

        var first = new RecordingThemeExtension("test.evicted", "First");
        first.Load();
        scope.Config.Theme = "test.evicted";
        Dispatcher.UIThread.RunJobs();
        Assert.That(first.AppliedCount, Is.EqualTo(1), "precondition: the first owner's theme was applied");

        ThemeRegistry.Register(
            new ThemeDescriptor(
                "test.evicted", "Broken", ThemeVariant.Light, new Uri("avares://NoSuchAssembly/Missing.axaml")));
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(Application.Current!.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Dark));
            Assert.That(first.RevertedCount, Is.EqualTo(1), "the evicted owner should be reverted");
        });
    }

    [AvaloniaTest]
    public void ApplyTheme_AfterFailedLoad_StillAppliesAnotherTheme()
    {
        using var scope = new ThemeScope();
        scope.Service.Start();
        Dispatcher.UIThread.RunJobs();

        var broken = new ThemeDescriptor(
            "test.broken2", "Broken", ThemeVariant.Light, new Uri("avares://NoSuchAssembly/Missing.axaml"));
        ThemeRegistry.Register(broken);

        scope.Config.Theme = "test.broken2";
        Dispatcher.UIThread.RunJobs();

        scope.Config.Theme = BuiltinThemeIds.Light;
        Dispatcher.UIThread.RunJobs();

        Assert.That(Application.Current!.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Light));
    }

    [AvaloniaTest]
    public void ApplyTheme_WhenResourceIsNotAResourceDictionary_LeavesCurrentThemeIntact()
    {
        // A root that loads but is not an IResourceProvider is a broken theme, not a theme without
        // resources: treating it as the latter drops the previous overrides and applies none.
        using var scope = new ThemeScope();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        scope.Service.Start();
        Dispatcher.UIThread.RunJobs();

        var wrongRoot = new ThemeDescriptor(
            "test.wrongroot", "WrongRoot", ThemeVariant.Light,
            new Uri("avares://Beutl.HeadlessUITests/Assets/NotAResourceDictionary.axaml"));
        ThemeRegistry.Register(wrongRoot);

        scope.Config.Theme = "test.wrongroot";
        Dispatcher.UIThread.RunJobs();

        Assert.That(Application.Current.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Dark));
    }

    [AvaloniaTest]
    public void UnregisteringActiveTheme_NotifiesItsOwner()
    {
        // Unload removes the theme from the registry before the host reverts, so resolving the owner
        // by id at notification time finds nothing and the extension never gets to release whatever
        // it set up in OnApplied.
        using var scope = new ThemeScope();
        scope.Service.Start();
        Dispatcher.UIThread.RunJobs();

        var ext = new RecordingThemeExtension("test.owned", "Owned");
        ext.Load();
        scope.Config.Theme = "test.owned";
        Dispatcher.UIThread.RunJobs();
        Assert.That(ext.AppliedCount, Is.EqualTo(1), "precondition: the extension's theme was applied");

        ext.Unload();
        Dispatcher.UIThread.RunJobs();

        Assert.That(ext.RevertedCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void ReregisteringActiveThemeWithNewOwner_NotifiesBothOwners()
    {
        // A package reload re-registers an equal-valued descriptor under the same id. The registry
        // keys ownership on the instance, so this is a new owner: the outgoing one must be reverted
        // and the incoming one applied, rather than the whole apply being skipped as "unchanged".
        using var scope = new ThemeScope();
        scope.Service.Start();
        Dispatcher.UIThread.RunJobs();

        var first = new RecordingThemeExtension("test.reload", "Reload");
        first.Load();
        scope.Config.Theme = "test.reload";
        Dispatcher.UIThread.RunJobs();
        Assert.That(first.AppliedCount, Is.EqualTo(1), "precondition: the first owner's theme was applied");

        var second = new RecordingThemeExtension("test.reload", "Reload");
        Assert.Multiple(() =>
        {
            Assert.That(second.GetThemeDescriptor(), Is.EqualTo(first.GetThemeDescriptor()),
                "precondition: the descriptors are equal-valued");
            Assert.That(second.GetThemeDescriptor(), Is.Not.SameAs(first.GetThemeDescriptor()),
                "precondition: the descriptors are distinct instances");
        });
        second.Load();
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(second.AppliedCount, Is.EqualTo(1), "the new owner should be applied");
            Assert.That(first.RevertedCount, Is.EqualTo(1), "the outgoing owner should be reverted");
        });
    }

    [AvaloniaTest]
    public void ThemeAccentColor_SeedsTheAccent_AndLeavesWithTheTheme()
    {
        using var scope = new ThemeScope();
        scope.Service.Start();
        Dispatcher.UIThread.RunJobs();

        var accent = Color.FromRgb(0x25, 0x63, 0xEB);
        var ext = new RecordingThemeExtension("test.accent", "Accent", accent);
        ext.Load();
        scope.Config.Theme = "test.accent";
        Dispatcher.UIThread.RunJobs();

        Assert.That(scope.Theme.CustomAccentColor, Is.EqualTo(accent),
            "the applied theme's design accent should seed FluentAvalonia's accent");

        scope.Config.Theme = BuiltinThemeIds.Dark;
        Dispatcher.UIThread.RunJobs();

        Assert.That(scope.Theme.CustomAccentColor, Is.Null,
            "built-ins carry no design accent, so the OS accent must come back");
    }

    [AvaloniaTest]
    public void UserCustomAccent_WinsOverThemeAccent_AndYieldsBackWhenDisabled()
    {
        using var scope = new ThemeScope();
        scope.Service.Start();
        Dispatcher.UIThread.RunJobs();

        var themeAccent = Color.FromRgb(0x25, 0x63, 0xEB);
        var custom = Color.FromRgb(0x10, 0x89, 0x3E);
        var ext = new RecordingThemeExtension("test.accent.custom", "Accent", themeAccent);
        ext.Load();
        scope.Config.Theme = "test.accent.custom";
        scope.Config.UseCustomAccentColor = true;
        scope.Config.CustomAccentColor = custom.ToString();
        Dispatcher.UIThread.RunJobs();

        Assert.That(scope.Theme.CustomAccentColor, Is.EqualTo(custom),
            "the user's custom accent must win over the theme's design accent");

        scope.Config.UseCustomAccentColor = false;
        Dispatcher.UIThread.RunJobs();

        Assert.That(scope.Theme.CustomAccentColor, Is.EqualTo(themeAccent),
            "disabling the custom accent must fall back to the theme's design accent");
    }

    private sealed class RecordingThemeExtension(string id, string name, Color? accentColor = null) : ThemeExtension
    {
        private readonly ThemeDescriptor _descriptor = new(id, name, ThemeVariant.Dark, AccentColor: accentColor);
        public int AppliedCount;
        public int RevertedCount;

        public override ThemeDescriptor GetThemeDescriptor() => _descriptor;

        public override void OnApplied(ThemeApplyContext context) => AppliedCount++;

        public override void OnReverted() => RevertedCount++;
    }

    // ThemeRegistry is a static global that ThemeService.Start seeds with the built-ins, so each
    // test both starts from and hands back an empty registry.
    private sealed class ThemeScope : IDisposable
    {
        public ThemeScope()
        {
            ClearRegistry();
            Theme = Application.Current!.Styles.OfType<FluentAvaloniaTheme>().Single();
            Config = new ViewConfig();
            Service = new ThemeService(Theme, Config);
        }

        public FluentAvaloniaTheme Theme { get; }

        public ViewConfig Config { get; }

        public ThemeService Service { get; }

        public void Dispose()
        {
            Service.Dispose();
            // The accent lands on the process-global FluentAvaloniaTheme; leaving it set would
            // bleed a test's accent into every later [AvaloniaTest] in the assembly.
            Theme.CustomAccentColor = null;
            ClearRegistry();
        }

        private static void ClearRegistry()
        {
            foreach (ThemeDescriptor descriptor in ThemeRegistry.Enumerate())
            {
                ThemeRegistry.Unregister(descriptor);
            }
        }
    }
}
