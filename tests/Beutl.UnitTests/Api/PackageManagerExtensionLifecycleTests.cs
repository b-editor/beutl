using System.Collections.Specialized;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Mcp;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

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
        FaultedDrainMcpToolExtension.Reset();
        LeaseManagedMcpToolExtension.Reset();
        MaterializingElementSourceHandlerExtension.Reset();
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
            if (provider.GetExtensions<LeaseManagedMcpToolExtension>().SingleOrDefault() is { } extension)
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
                [typeof(LeaseManagedMcpToolExtension)]));

        Assert.That(LeaseManagedMcpToolExtension.UnloadCount, Is.Zero);
        releaseLease.SetResult();
        Assert.That(
            SpinWait.SpinUntil(
                () => LeaseManagedMcpToolExtension.UnloadCount == 1,
                TimeSpan.FromSeconds(5)),
            Is.True);
        await Task.Yield();
        Assert.That(manager.LoadedPackage, Is.Empty);
    }

    [Test]
    public async Task LoadExtensionsAndRegister_DoesNotPublishProvisionalHandler()
    {
        PackageManager manager = CreatePackageManager(
            out _,
            out ExtensionProvider provider);
        await using var handlers = new ElementSourceHandlerRegistry([], provider);
        var package = new LocalPackage { Name = "ProvisionalMaterialization" };
        bool provisionalAcquired = false;
        provider.AllExtensions.CollectionChanged += (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add)
                return;

            provisionalAcquired = handlers.TryAcquire(
                typeof(MaterializedSource),
                out IElementSourceHandlerLease? lease);
            if (lease is not null)
            {
                lease.Dispose();
            }
        };
        provider.AllExtensions.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
                throw new InvalidOperationException("observer failure after materialization");
        };

        Assert.Throws<ExtensionRegistrationNotificationException>(() =>
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(MaterializingElementSourceHandlerExtension)]));

        Assert.That(
            SpinWait.SpinUntil(
                () => MaterializingElementSourceHandlerExtension.UnloadCount == 1,
                TimeSpan.FromSeconds(5)),
            Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(provisionalAcquired, Is.False);
            Assert.That(handlers.TryAcquire(typeof(MaterializedSource), out _), Is.False);
            Assert.That(manager.LoadedPackage, Is.Empty);
        }
    }

    [Test]
    public async Task LoadExtensionsAndRegister_UnloadsCommittedMaterializationOnLaterFailure()
    {
        PackageManager manager = CreatePackageManager(
            out _,
            out ExtensionProvider provider);
        await using var handlers = new ElementSourceHandlerRegistry([], provider);
        var package = new LocalPackage { Name = "CommittedMaterialization" };
        Element? retainedGraph = null;
        manager.AfterExtensionRegistration = () =>
        {
            retainedGraph = MaterializePackageGraph(handlers);
            throw new InvalidOperationException("failure after extension registration");
        };

        Assert.Throws<InvalidOperationException>(() =>
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(MaterializingElementSourceHandlerExtension)]));

        Assert.That(
            SpinWait.SpinUntil(
                () => MaterializingElementSourceHandlerExtension.UnloadCount == 1,
                TimeSpan.FromSeconds(5)),
            Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retainedGraph, Is.Not.Null);
            Assert.That(
                retainedGraph!.Objects.Single(),
                Is.TypeOf<MaterializedPackageObject>());
            Assert.That(handlers.TryAcquire(typeof(MaterializedSource), out _), Is.False);
            Assert.That(manager.LoadedPackage, Is.Empty);
        }
    }

    [Test]
    public async Task Unload_WaitsForPublicExtensionLeaseBeforeCallingExtensionUnload()
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        IExtensionProvider publicProvider = provider;
        var package = new LocalPackage { Name = "PublicLease" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(LeaseManagedMcpToolExtension)]);
        ExtensionDescriptor descriptor = publicProvider
            .GetDescriptors<LeaseManagedMcpToolExtension>()
            .Single();
        Assert.That(
            publicProvider.TryAcquire(
                descriptor.Id,
                out IExtensionLease<LeaseManagedMcpToolExtension>? lease),
            Is.True);

        Task<bool> unload = manager.Unload(package).AsTask();
        try
        {
            await Task.Delay(100);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(unload.IsCompleted, Is.False);
                Assert.That(LeaseManagedMcpToolExtension.UnloadCount, Is.Zero);
                Assert.That(publicProvider.Extensions, Is.Empty);
                Assert.That(
                    publicProvider.TryAcquire(
                        descriptor.Id,
                        out IExtensionLease<LeaseManagedMcpToolExtension>? retiredLease),
                    Is.False);
                Assert.That(retiredLease, Is.Null);
            }
        }
        finally
        {
            lease!.Dispose();
        }

        Assert.That(await unload.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(LeaseManagedMcpToolExtension.UnloadCount, Is.EqualTo(1));
    }

    private static IEnumerable<TestCaseData> ExtensionKinds()
    {
        yield return new TestCaseData(
                typeof(SuccessfulViewExtension),
                (Func<int>)(() => SuccessfulViewExtension.UnloadCount))
            .SetName("Unload_UnloadsViewExtensionsLive");
        yield return new TestCaseData(
                typeof(MaterializingElementSourceHandlerExtension),
                (Func<int>)(() => MaterializingElementSourceHandlerExtension.UnloadCount))
            .SetName("Unload_UnloadsElementSourceHandlerExtensionsLive");
    }

    [TestCaseSource(nameof(ExtensionKinds))]
    public async Task Unload_UnloadsExtensionLive(Type extensionType, Func<int> unloadCount)
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        var package = new LocalPackage { Name = extensionType.Name };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [extensionType]);

        bool unloaded = await manager.Unload(package);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unloaded, Is.True);
            Assert.That(unloadCount(), Is.EqualTo(1));
            Assert.That(provider.AllExtensions.Count(extension => extension.GetType() == extensionType), Is.Zero);
            Assert.That(manager.LoadedPackage, Does.Not.Contain(package));
        }
    }

    [Test]
    public async Task Unload_ReportsFailure_WhenTheLoadContextStaysAlive()
    {
        PackageManager manager = CreatePackageManager(out _, out _);
        var package = new LocalPackage { Name = "RetainedContext" };
        var loadContext = new PluginLoadContext(AppContext.BaseDirectory);
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext,
            [typeof(SuccessfulViewExtension)]);

        bool unloaded = await manager.Unload(package);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unloaded, Is.False);
            Assert.That(SuccessfulViewExtension.UnloadCount, Is.EqualTo(1));
            Assert.That(manager.LoadedPackage, Does.Not.Contain(package));
        }
        GC.KeepAlive(loadContext);
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
            [typeof(FaultedDrainMcpToolExtension)]);
        FaultedDrainMcpToolExtension extension = provider
            .GetExtensions<FaultedDrainMcpToolExtension>()
            .Single();
        ExtensionRegistrationLifetimes.Retire(
            extension,
            () => new ValueTask(Task.FromException(
                new InvalidOperationException("synthetic drain failure"))));

        bool unloaded = await manager.Unload(package);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unloaded, Is.False);
            Assert.That(FaultedDrainMcpToolExtension.UnloadCount, Is.Zero);
            Assert.That(provider.GetExtensions<FaultedDrainMcpToolExtension>(), Is.Empty);
            Assert.That(manager.LoadedPackage, Does.Contain(package));
        }

        Assert.That(await manager.Unload(package), Is.False);
        Assert.That(FaultedDrainMcpToolExtension.UnloadCount, Is.Zero);
    }

    private static Element MaterializePackageGraph(IElementSourceHandlerRegistry handlers)
    {
        if (!handlers.TryAcquire(
                typeof(MaterializedSource),
                out IElementSourceHandlerLease? lease))
        {
            throw new InvalidOperationException(
                "The committed element source handler was not available.");
        }

        using (lease)
        {
            var scene = new Scene(640, 480, "rollback");
            var description = new ElementDescription(
                TimeSpan.Zero,
                null,
                0,
                new MaterializedSource());
            ElementSourcePreflightResult preflight = lease.Handler.PreflightAsync(
                new ElementSourcePreflightContext(scene, description),
                CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                ElementSourceMaterializationResult materialized = lease.Handler.MaterializeAsync(
                    new ElementSourceMaterializationContext(scene, description),
                    preflight.Preflight!,
                    CancellationToken.None).GetAwaiter().GetResult();
                return materialized.Materialization!.PrimaryElement;
            }
            finally
            {
                preflight.Preflight!.DisposeAsync().GetAwaiter().GetResult();
            }
        }
    }

    private static PackageManager CreatePackageManager(
        out ContextCommandManager commandManager,
        out ExtensionProvider extensionProvider)
    {
        commandManager = new ContextCommandManager(new ContextCommandSettingsStore());
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
            [new("test.success-command")];

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
            [new("test.failing-command")];

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
    private sealed class FaultedDrainMcpToolExtension : McpToolExtension
    {
        public static int UnloadCount { get; private set; }

        public override IReadOnlyList<McpToolDefinition> Tools => [];

        public override ValueTask<McpToolResult> InvokeAsync(McpToolCall call, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public static void Reset() => UnloadCount = 0;

        public override void Unload() => UnloadCount++;
    }

    [Export]
    private sealed class LeaseManagedMcpToolExtension : McpToolExtension
    {
        public static int UnloadCount { get; private set; }

        public override IReadOnlyList<McpToolDefinition> Tools => [];

        public override ValueTask<McpToolResult> InvokeAsync(McpToolCall call, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override void Unload() => UnloadCount++;

        public static void Reset() => UnloadCount = 0;
    }

    private sealed record MaterializedSource : ElementSource;

    [Export]
    private sealed class MaterializingElementSourceHandlerExtension : ElementSourceHandlerExtension
    {
        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<ElementSourceHandlerRegistration> Registrations { get; } =
        [
            new ElementSourceHandlerRegistration(new MaterializingElementSourceHandler()),
        ];

        public override void Unload() => UnloadCount++;

        public static void Reset() => UnloadCount = 0;
    }

    private sealed class MaterializingElementSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(MaterializedSource);

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
                MaterializedSourcePreflight.Instance,
                [context.Description.Layer]));
        }

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var element = new Element
            {
                Start = context.Description.Start,
                Length = TimeSpan.FromSeconds(3),
                ZIndex = context.Description.Layer,
            };
            element.AddObject(new MaterializedPackageObject());
            return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(element)));
        }
    }

    private sealed class MaterializedSourcePreflight : IElementSourcePreflight
    {
        public static MaterializedSourcePreflight Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
}

public sealed partial class MaterializedPackageObject : EngineObject;
