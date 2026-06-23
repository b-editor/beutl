using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class PackageManagerExtensionLifecycleTests
{
    [SetUp]
    public void SetUp()
    {
        SuccessfulViewExtension.Reset();
        FailingViewExtension.Reset();
    }

    [Test]
    public void LoadPackageExtensions_RollsBackLoadedExtensions_WhenLaterExtensionFails()
    {
        PackageManager manager = CreatePackageManager(out ContextCommandManager commandManager, out _);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.LoadPackageExtensions(
                [typeof(SuccessfulViewExtension), typeof(FailingViewExtension)]));

        Assert.That(exception!.Message, Is.EqualTo("boom"));
        Assert.That(commandManager.GetDefinitions(typeof(SuccessfulViewExtension)), Is.Empty);
        Assert.That(commandManager.GetDefinitions(typeof(FailingViewExtension)), Is.Empty);
        Assert.That(SuccessfulViewExtension.LoadCount, Is.EqualTo(1));
        Assert.That(SuccessfulViewExtension.UnloadCount, Is.EqualTo(1));
        Assert.That(FailingViewExtension.LoadCount, Is.EqualTo(1));
        Assert.That(FailingViewExtension.UnloadCount, Is.EqualTo(1));
    }

    [Test]
    public void LoadExtensionsAndRegister_RegistersPackage_OnSuccess()
    {
        PackageManager manager = CreatePackageManager(out ContextCommandManager commandManager, out ExtensionProvider provider);
        var package = new LocalPackage { Name = "Successful" };

        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(SuccessfulViewExtension)]);

        Assert.That(SuccessfulViewExtension.LoadCount, Is.EqualTo(1));
        Assert.That(SuccessfulViewExtension.UnloadCount, Is.EqualTo(0));
        Assert.That(provider.GetExtensions<SuccessfulViewExtension>(), Has.Length.EqualTo(1));
        Assert.That(manager.LoadedPackage, Does.Contain(package));
        Assert.That(commandManager.GetDefinitions(typeof(SuccessfulViewExtension)), Is.Not.Empty);
    }

    [Test]
    public void LoadExtensionsAndRegister_RollsBackNewExtensions_WhenPackageIdAlreadyRegistered()
    {
        PackageManager manager = CreatePackageManager(out ContextCommandManager commandManager, out ExtensionProvider provider);
        var package = new LocalPackage { Name = "Duplicate" };

        // Pre-register the package id so AddExtensions rejects the load as a duplicate.
        provider.AddExtensions(package.LocalId, []);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(SuccessfulViewExtension)]));

        Assert.That(exception!.Message, Does.Contain("already registered"));
        Assert.That(SuccessfulViewExtension.LoadCount, Is.EqualTo(1));
        Assert.That(SuccessfulViewExtension.UnloadCount, Is.EqualTo(1));
        Assert.That(commandManager.GetDefinitions(typeof(SuccessfulViewExtension)), Is.Empty);
        Assert.That(manager.LoadedPackage, Is.Empty);
    }

    [Test]
    public void LoadExtensionsAndRegister_RollsBackNewExtensions_WhenPackageAlreadyTracked()
    {
        PackageManager manager = CreatePackageManager(out ContextCommandManager commandManager, out ExtensionProvider provider);
        var package = new LocalPackage { Name = "Tracked" };

        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(SuccessfulViewExtension)]);

        // Drop only the provider entry so the second load passes AddExtensions but trips the
        // _loadedPackages.TryAdd guard, exercising the "already loaded" rollback branch.
        provider.RemoveExtensions(package.LocalId);
        SuccessfulViewExtension.Reset();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(SuccessfulViewExtension)]));

        Assert.That(exception!.Message, Does.Contain("already loaded"));
        Assert.That(SuccessfulViewExtension.LoadCount, Is.EqualTo(1));
        Assert.That(SuccessfulViewExtension.UnloadCount, Is.EqualTo(1));
        Assert.That(provider.GetExtensions<SuccessfulViewExtension>(), Is.Empty);
        Assert.That(commandManager.GetDefinitions(typeof(SuccessfulViewExtension)), Is.Empty);
    }

    private static PackageManager CreatePackageManager(
        out ContextCommandManager commandManager,
        out ExtensionProvider extensionProvider)
    {
        commandManager = new ContextCommandManager(
            new ContextCommandSettingsStore(),
            new ContextCommandHandlerRegistry());
        extensionProvider = new ExtensionProvider();

        return new PackageManager(
            new InstalledPackageRepository(),
            extensionProvider,
            commandManager,
            apiApplication: null!);
    }

    // Nested + private so the app's exported-type scan never picks these up; [Export] stays because
    // LoadExtension filters candidate types on it.
    [Export]
    private sealed class SuccessfulViewExtension : ViewExtension
    {
        public static int LoadCount { get; private set; }

        public static int UnloadCount { get; private set; }

        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
            [new("success-command")];

        public static void Reset()
        {
            LoadCount = 0;
            UnloadCount = 0;
        }

        public override void Load()
        {
            LoadCount++;
        }

        public override void Unload()
        {
            UnloadCount++;
        }
    }

    [Export]
    private sealed class FailingViewExtension : ViewExtension
    {
        public static int LoadCount { get; private set; }

        public static int UnloadCount { get; private set; }

        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
            [new("failing-command")];

        public static void Reset()
        {
            LoadCount = 0;
            UnloadCount = 0;
        }

        public override void Load()
        {
            LoadCount++;
            throw new InvalidOperationException("boom");
        }

        public override void Unload()
        {
            UnloadCount++;
        }
    }
}
