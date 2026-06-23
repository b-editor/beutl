using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.UnitTests.Api;

[TestFixture]
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
        PackageManager manager = CreatePackageManager(out ContextCommandManager commandManager);
        var extensions = new List<Extension>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.LoadPackageExtensions(
                [typeof(SuccessfulViewExtension), typeof(FailingViewExtension)],
                extensions));

        Assert.That(exception!.Message, Is.EqualTo("boom"));
        Assert.That(extensions, Is.Empty);
        Assert.That(commandManager.GetDefinitions(typeof(SuccessfulViewExtension)), Is.Empty);
        Assert.That(commandManager.GetDefinitions(typeof(FailingViewExtension)), Is.Empty);
        Assert.That(SuccessfulViewExtension.LoadCount, Is.EqualTo(1));
        Assert.That(SuccessfulViewExtension.UnloadCount, Is.EqualTo(1));
        Assert.That(FailingViewExtension.LoadCount, Is.EqualTo(1));
        Assert.That(FailingViewExtension.UnloadCount, Is.EqualTo(1));
    }

    private static PackageManager CreatePackageManager(out ContextCommandManager commandManager)
    {
        commandManager = new ContextCommandManager(
            new ContextCommandSettingsStore(),
            new ContextCommandHandlerRegistry());

        return new PackageManager(
            new InstalledPackageRepository(),
            new ExtensionProvider(),
            commandManager,
            apiApplication: null!);
    }
}

[Export]
public sealed class SuccessfulViewExtension : ViewExtension
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
public sealed class FailingViewExtension : ViewExtension
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
