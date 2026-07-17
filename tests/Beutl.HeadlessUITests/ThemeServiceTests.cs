using System.Linq;
using Avalonia;
using Avalonia.Headless.NUnit;
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
        Assert.That(second.GetThemeDescriptor(), Is.EqualTo(first.GetThemeDescriptor()),
            "precondition: the descriptors are equal-valued but distinct instances");
        second.Load();
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(second.AppliedCount, Is.EqualTo(1), "the new owner should be applied");
            Assert.That(first.RevertedCount, Is.EqualTo(1), "the outgoing owner should be reverted");
        });
    }

    private sealed class RecordingThemeExtension(string id, string name) : ThemeExtension
    {
        private readonly ThemeDescriptor _descriptor = new(id, name, ThemeVariant.Dark);
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
            FluentAvaloniaTheme theme = Application.Current!.Styles.OfType<FluentAvaloniaTheme>().Single();
            Config = new ViewConfig();
            Service = new ThemeService(theme, Config);
        }

        public ViewConfig Config { get; }

        public ThemeService Service { get; }

        public void Dispose()
        {
            Service.Dispose();
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
