using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Editor.Models;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class PackageManagerExtensionLifecycleTests
{
    private static readonly CaptionFormatId s_blockingCaptionFormat = new("beutl.tests.blocking");
    private static readonly CaptionTemplateId s_blockingCaptionTemplate =
        new("beutl.tests.blocking-template");

    [SetUp]
    public void SetUp()
    {
        SuccessfulViewExtension.Reset();
        FailingViewExtension.Reset();
        BlockingCaptionCodecExtension.Reset();
        BlockingCaptionTemplateExtension.Reset();
        FaultedDrainViewExtension.Reset();
        LeaseManagedAiJobKindExtension.Reset();
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

        // Drop only the provider entry. The second load rejects the already tracked package
        // before exposing its new extensions to observers.
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
        Assert.That(SuccessfulViewExtension.LoadCount, Is.Zero);
        Assert.That(SuccessfulViewExtension.UnloadCount, Is.Zero);
        Assert.That(provider.GetExtensions<SuccessfulViewExtension>(), Is.Empty);
        Assert.That(commandManager.GetDefinitions(typeof(SuccessfulViewExtension)), Is.Not.Empty);
    }

    [Test]
    public void LoadExtensionsAndRegister_UnloadsTheLoadContextOfARejectedDuplicate()
    {
        PackageManager manager = CreatePackageManager(out _, out _);
        var package = new LocalPackage { Name = "DuplicateWithContext" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(SuccessfulViewExtension)]);

        // The assemblies of a second load are resolved into a collectible
        // context before the duplicate is noticed. Left loaded, that context —
        // and everything in it — stays for the life of the process.
        var loadContext = new PluginLoadContext(AppContext.BaseDirectory);
        var unloaded = false;
        loadContext.Unloading += _ => unloaded = true;

        Assert.Throws<InvalidOperationException>(() =>
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: loadContext,
                [typeof(SuccessfulViewExtension)]));

        Assert.That(unloaded, Is.True);
    }

    [Test]
    public async Task LoadExtensionsAndRegister_DoesNotExecutePluginCodeForConcurrentDuplicateLoad()
    {
        PackageManager manager = CreatePackageManager(out _, out _);
        var package = new LocalPackage { Name = "ConcurrentDuplicate" };
        using var loadStarted = new ManualResetEventSlim();
        using var releaseLoad = new ManualResetEventSlim();
        BlockingLoadViewExtension.Configure(loadStarted, releaseLoad);

        Task first = Task.Run(() => manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(BlockingLoadViewExtension)]));
        Assert.That(loadStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(SuccessfulViewExtension)]));
        releaseLoad.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(exception!.Message, Does.Contain("loading"));
        Assert.That(SuccessfulViewExtension.LoadCount, Is.Zero);
    }

    [Test]
    public async Task LoadExtensionsAndRegister_DrainsObserverLeaseBeforeRollbackUnload()
    {
        PackageManager manager = CreatePackageManager(
            out _,
            out ExtensionProvider provider);
        var package = new LocalPackage { Name = "ObserverRollback" };
        var releaseLease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AllExtensions.CollectionChanged += (_, _) =>
        {
            if (provider.GetExtensions<LeaseManagedAiJobKindExtension>().SingleOrDefault() is { } extension)
            {
                ExtensionRegistrationLifetimes.Retire(
                    extension,
                    () => new ValueTask(releaseLease.Task));
            }
        };
        provider.AllExtensions.CollectionChanged += (_, _) =>
            throw new InvalidOperationException("observer failure");

        Assert.Throws<ExtensionRegistrationNotificationException>(() =>
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(LeaseManagedAiJobKindExtension)]));

        Assert.That(LeaseManagedAiJobKindExtension.UnloadCount, Is.Zero);
        releaseLease.SetResult();
        Assert.That(
            SpinWait.SpinUntil(
                () => LeaseManagedAiJobKindExtension.UnloadCount == 1,
                TimeSpan.FromSeconds(5)),
            Is.True);
        await Task.Yield();
        Assert.That(manager.LoadedPackage, Is.Empty);
    }

    [Test]
    public async Task Unload_DoesNotCaptureDiagnostics_WhenPackageUnloadsCleanly()
    {
        var diagnostics = new RecordingUnloadDiagnostics();
        PackageManager manager = CreatePackageManager(diagnostics, out _, out _);
        var package = new LocalPackage { Name = "CleanUnload" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(LeaseManagedAiJobKindExtension)]);

        bool unloaded = await manager.Unload(package);

        Assert.Multiple(() =>
        {
            // No load context means nothing pins the package, so the unload succeeds and diagnostics stay idle.
            Assert.That(unloaded, Is.True);
            Assert.That(diagnostics.InvokeCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Unload_SupportsAlternativeExtensionRegistryImplementations()
    {
        var registry = new DelegatingExtensionRegistry();
        PackageManager manager = CreatePackageManager(registry, out _);
        var package = new LocalPackage { Name = "AlternativeRegistry" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(LeaseManagedAiJobKindExtension)]);

        bool unloaded = await manager.Unload(package);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unloaded, Is.True);
            Assert.That(registry.SynchronizationCount, Is.GreaterThan(0));
            Assert.That(registry.GetExtensions<LeaseManagedAiJobKindExtension>(), Is.Empty);
            Assert.That(LeaseManagedAiJobKindExtension.UnloadCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Unload_RequiresRestartForLongLivedExtensionFamilies()
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        var package = new LocalPackage { Name = "LongLivedView" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(SuccessfulViewExtension)]);

        bool unloaded = await manager.Unload(package);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unloaded, Is.False);
            Assert.That(SuccessfulViewExtension.UnloadCount, Is.Zero);
            Assert.That(provider.GetExtensions<SuccessfulViewExtension>(), Has.Length.EqualTo(1));
            Assert.That(manager.LoadedPackage, Does.Contain(package));
        }
    }

    [Test]
    public async Task Unload_WaitsForActiveCaptionLeaseBeforeCallingExtensionUnload()
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], provider);
        var package = new LocalPackage { Name = "BlockingCaption" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(BlockingCaptionCodecExtension)]);

        Task<CaptionImportResult> decodeTask = Task.Run(() =>
            catalog.Serializer.Import("caption"u8, s_blockingCaptionFormat));
        Assert.That(
            BlockingCaptionCodecExtension.WaitForDecode(TimeSpan.FromSeconds(5)),
            Is.True,
            "The test codec did not begin decoding.");

        Task<bool> unloadTask = Task.Run(async () => await manager.Unload(package));
        try
        {
            Assert.That(
                SpinWait.SpinUntil(
                    () => provider.GetExtensions<BlockingCaptionCodecExtension>().Length == 0,
                    TimeSpan.FromSeconds(5)),
                Is.True,
                "Package removal did not begin.");
            Assert.Multiple(() =>
            {
                Assert.That(unloadTask.IsCompleted, Is.False);
                Assert.That(BlockingCaptionCodecExtension.UnloadCount, Is.EqualTo(0));
            });
        }
        finally
        {
            BlockingCaptionCodecExtension.ReleaseDecode();
        }

        CaptionImportResult decoded = await decodeTask.WaitAsync(TimeSpan.FromSeconds(5));
        bool unloaded = await unloadTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(decoded.IsSuccess, Is.True);
            Assert.That(unloaded, Is.True);
            Assert.That(BlockingCaptionCodecExtension.UnloadCount, Is.EqualTo(1));
            Assert.Throws<KeyNotFoundException>(() =>
                catalog.Serializer.Import("after unload"u8, s_blockingCaptionFormat));
        });
    }

    [Test]
    public async Task Unload_WaitsForActiveCaptionTemplateLeaseBeforeCallingExtensionUnload()
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], provider);
        var package = new LocalPackage { Name = "BlockingCaptionTemplate" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(BlockingCaptionTemplateExtension)]);

        Task<IReadOnlyList<ElementDescription>> createTask = Task.Run(() =>
        {
            using CaptionTemplateLease lease = catalog.Templates.Acquire(s_blockingCaptionTemplate);
            return lease.CreateElements(
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "caption"),
                new CaptionElementContext(0, "Caption"));
        });
        Assert.That(
            BlockingCaptionTemplateExtension.WaitForCreate(TimeSpan.FromSeconds(5)),
            Is.True,
            "The test caption template did not begin creating elements.");

        Task<bool> unloadTask = Task.Run(async () => await manager.Unload(package));
        try
        {
            Assert.That(
                SpinWait.SpinUntil(
                    () => provider.GetExtensions<BlockingCaptionTemplateExtension>().Length == 0,
                    TimeSpan.FromSeconds(5)),
                Is.True,
                "Package removal did not begin.");
            Assert.Multiple(() =>
            {
                Assert.That(unloadTask.IsCompleted, Is.False);
                Assert.That(BlockingCaptionTemplateExtension.UnloadCount, Is.EqualTo(0));
            });
        }
        finally
        {
            BlockingCaptionTemplateExtension.ReleaseCreate();
        }

        IReadOnlyList<ElementDescription> descriptions =
            await createTask.WaitAsync(TimeSpan.FromSeconds(5));
        bool unloaded = await unloadTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(descriptions, Has.Count.EqualTo(1));
            Assert.That(unloaded, Is.True);
            Assert.That(BlockingCaptionTemplateExtension.UnloadCount, Is.EqualTo(1));
            Assert.Throws<KeyNotFoundException>(() =>
                catalog.Templates.Acquire(s_blockingCaptionTemplate));
        });
    }

    [Test]
    public async Task Unload_QuarantinesPackageWithoutCallingUnload_WhenRegistrationDrainFails()
    {
        PackageManager manager = CreatePackageManager(
            out ContextCommandManager commandManager,
            out ExtensionProvider provider);
        var package = new LocalPackage { Name = "FaultedDrain" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(FaultedDrainViewExtension)]);
        FaultedDrainViewExtension extension = provider
            .GetExtensions<FaultedDrainViewExtension>()
            .Single();
        ExtensionRegistrationLifetimes.Retire(
            extension,
            () => new ValueTask(Task.FromException(
                new InvalidOperationException("synthetic drain failure"))));

        bool unloaded = await manager.Unload(package);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unloaded, Is.False);
            Assert.That(FaultedDrainViewExtension.UnloadCount, Is.Zero);
            Assert.That(provider.GetExtensions<FaultedDrainViewExtension>(), Is.Empty);
            Assert.That(manager.LoadedPackage, Does.Contain(package));
        }

        Assert.That(await manager.Unload(package), Is.False);
        Assert.That(FaultedDrainViewExtension.UnloadCount, Is.Zero);
    }

    private static PackageManager CreatePackageManager(
        out ContextCommandManager commandManager,
        out ExtensionProvider extensionProvider)
    {
        return CreatePackageManager(diagnostics: null, out commandManager, out extensionProvider);
    }

    private static PackageManager CreatePackageManager(
        ILoadContextUnloadDiagnostics? diagnostics,
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
            apiApplication: null!,
            diagnostics);
    }

    private static PackageManager CreatePackageManager(
        IExtensionRegistry extensionRegistry,
        out ContextCommandManager commandManager)
    {
        commandManager = new ContextCommandManager(
            new ContextCommandSettingsStore(),
            new ContextCommandHandlerRegistry());
        return new PackageManager(
            new InstalledPackageRepository(),
            extensionRegistry,
            commandManager,
            apiApplication: null!);
    }

    private sealed class DelegatingExtensionRegistry : IExtensionRegistry
    {
        private readonly ExtensionProvider _inner = new();

        public int SynchronizationCount { get; private set; }

        public ICoreReadOnlyList<Extension> AllExtensions => _inner.AllExtensions;

        public void AddExtensions(int packageId, IReadOnlyList<Extension> extensions)
            => _inner.AddExtensions(packageId, extensions);

        public IReadOnlyList<Extension> GetPackageExtensions(int packageId)
            => _inner.GetPackageExtensions(packageId);

        public TExtension[] GetExtensions<TExtension>()
            where TExtension : Extension
            => _inner.GetExtensions<TExtension>();

        public EditorExtension? MatchEditorExtension(string file)
            => _inner.MatchEditorExtension(file);

        public ExtensionRemoval RemoveExtensions(int packageId)
            => _inner.RemoveExtensions(packageId);

        public void SynchronizeMutation(Action action)
        {
            SynchronizationCount++;
            _inner.SynchronizeMutation(action);
        }
    }

    private sealed class RecordingUnloadDiagnostics : ILoadContextUnloadDiagnostics
    {
        public int InvokeCount { get; private set; }

        public string? CaptureUnloadFailure(string packageName, IReadOnlyList<string> assemblySimpleNames)
        {
            InvokeCount++;
            return null;
        }
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

    [Export]
    private sealed class FaultedDrainViewExtension : AiJobKindExtension
    {
        public static int UnloadCount { get; private set; }

        public override AiJobKindDescriptor Descriptor { get; } = new(
            new AiJobKindId("beutl.tests.faulted-drain"),
            new AiJobStatusMap([]));

        public override AiJobKindRegistrationMode RegistrationMode
            => AiJobKindRegistrationMode.Add;

        public static void Reset() => UnloadCount = 0;

        public override void Unload() => UnloadCount++;
    }

    [Export]
    private sealed class LeaseManagedAiJobKindExtension : AiJobKindExtension
    {
        public static int UnloadCount { get; private set; }

        public override AiJobKindDescriptor Descriptor { get; } = new(
            new AiJobKindId("beutl.tests.lease-managed"),
            new AiJobStatusMap([]));

        public override AiJobKindRegistrationMode RegistrationMode
            => AiJobKindRegistrationMode.Add;

        public override void Unload() => UnloadCount++;

        public static void Reset() => UnloadCount = 0;
    }

    [Export]
    private sealed class BlockingLoadViewExtension : ViewExtension
    {
        private static ManualResetEventSlim? s_loadStarted;
        private static ManualResetEventSlim? s_releaseLoad;

        public static void Configure(
            ManualResetEventSlim loadStarted,
            ManualResetEventSlim releaseLoad)
        {
            s_loadStarted = loadStarted;
            s_releaseLoad = releaseLoad;
        }

        public override void Load()
        {
            s_loadStarted!.Set();
            if (!s_releaseLoad!.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocking extension load was not released.");
        }
    }

    [Export]
    private sealed class BlockingCaptionCodecExtension : CaptionCodecExtension, ICaptionDecoder
    {
        private static readonly ManualResetEventSlim s_decodeStarted = new();
        private static readonly ManualResetEventSlim s_releaseDecode = new();
        private readonly IReadOnlyCollection<CaptionCodecRegistration> _registrations;

        public BlockingCaptionCodecExtension()
        {
            _registrations =
            [
                new CaptionCodecRegistration(new CaptionCodecContribution(
                    s_blockingCaptionFormat,
                    new CaptionCodecDescriptor(s_blockingCaptionFormat, [".blocking-caption"]),
                    decoder: this)),
            ];
        }

        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<CaptionCodecRegistration> Registrations
            => _registrations;

        public CaptionImportResult Decode(string content)
        {
            s_decodeStarted.Set();
            if (!s_releaseDecode.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocking caption codec was not released.");

            return CaptionImportResult.Imported(new CaptionDocument(
            [
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), content),
            ]));
        }

        public override void Unload()
        {
            UnloadCount++;
        }

        public static bool WaitForDecode(TimeSpan timeout) => s_decodeStarted.Wait(timeout);

        public static void ReleaseDecode() => s_releaseDecode.Set();

        public static void Reset()
        {
            UnloadCount = 0;
            s_decodeStarted.Reset();
            s_releaseDecode.Reset();
        }
    }

    [Export]
    private sealed class BlockingCaptionTemplateExtension : CaptionTemplateExtension, ICaptionElementFactory
    {
        private static readonly ManualResetEventSlim s_createStarted = new();
        private static readonly ManualResetEventSlim s_releaseCreate = new();
        private readonly IReadOnlyCollection<CaptionTemplateRegistration> _registrations;

        public BlockingCaptionTemplateExtension()
        {
            _registrations =
            [
                new CaptionTemplateRegistration(new CaptionTemplateContribution(
                    s_blockingCaptionTemplate,
                    new CaptionTemplateProviderId("beutl.tests"),
                    "Blocking caption template",
                    this,
                    DefaultCaptionPlacementPolicy.Instance)),
            ];
        }

        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<CaptionTemplateRegistration> Registrations
            => _registrations;

        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
        {
            s_createStarted.Set();
            if (!s_releaseCreate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocking caption template was not released.");

            return
            [
                context.CreateDescription(
                    cue,
                    () => new TextBlock { Text = { CurrentValue = cue.Text } }),
            ];
        }

        public override void Unload()
        {
            UnloadCount++;
        }

        public static bool WaitForCreate(TimeSpan timeout) => s_createStarted.Wait(timeout);

        public static void ReleaseCreate() => s_releaseCreate.Set();

        public static void Reset()
        {
            UnloadCount = 0;
            s_createStarted.Reset();
            s_releaseCreate.Reset();
        }
    }
}
