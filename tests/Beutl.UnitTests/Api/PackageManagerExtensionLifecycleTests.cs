using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Editor.Services.Captions;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public class PackageManagerExtensionLifecycleTests
{
    private static readonly CaptionFormatId s_blockingCaptionFormat = new("beutl.tests.blocking");
    private static readonly CaptionTemplateId s_restartOnlyCaptionTemplate =
        new("beutl.tests.restart-only-template");

    [SetUp]
    public void SetUp()
    {
        SuccessfulViewExtension.Reset();
        FailingViewExtension.Reset();
        BlockingCaptionDecoderExtension.Reset();
        RestartOnlyCaptionElementFactoryExtension.Reset();
        RestartOnlyAiJobResultApplicatorExtension.Reset();
        LeaseManagedAiJobPresentationExtension.Reset();
        LeaseManagedAiJobCompletionExtension.Reset();
        LeaseManagedCaptionTemplateDescriptorExtension.Reset();
        LeaseManagedCaptionPlacementExtension.Reset();
        FaultedDrainViewExtension.Reset();
        LeaseManagedAiJobStatusResolverExtension.Reset();
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
            if (provider.GetExtensions<LeaseManagedAiJobStatusResolverExtension>().SingleOrDefault() is { } extension)
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
                [typeof(LeaseManagedAiJobStatusResolverExtension)]));

        Assert.That(LeaseManagedAiJobStatusResolverExtension.UnloadCount, Is.Zero);
        releaseLease.SetResult();
        Assert.That(
            SpinWait.SpinUntil(
                () => LeaseManagedAiJobStatusResolverExtension.UnloadCount == 1,
                TimeSpan.FromSeconds(5)),
            Is.True);
        await Task.Yield();
        Assert.That(manager.LoadedPackage, Is.Empty);
    }

    [Test]
    public async Task LoadExtensionsAndRegister_DoesNotPublishProvisionalRestartOnlyHandler()
    {
        PackageManager manager = CreatePackageManager(
            out _,
            out ExtensionProvider provider);
        await using var handlers = new ElementSourceHandlerRegistry([], provider);
        var package = new LocalPackage { Name = "ProvisionalMaterialization" };
        bool provisionalAcquired = false;
        int metadataChanges = 0;
        handlers.Handlers.CollectionChanged += (_, _) => metadataChanges++;
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
        await Task.Delay(100);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provisionalAcquired, Is.False);
            Assert.That(handlers.Handlers, Is.Empty);
            Assert.That(metadataChanges, Is.Zero);
            Assert.That(MaterializingElementSourceHandlerExtension.UnloadCount, Is.Zero);
            Assert.That(manager.LoadedPackage, Is.Empty);
        }
        InvalidOperationException? retry = Assert.Throws<InvalidOperationException>(() =>
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(MaterializingElementSourceHandlerExtension)]));
        Assert.That(retry!.Message, Does.Contain("quarantined"));
    }

    [Test]
    public async Task LoadExtensionsAndRegister_QuarantinesCommittedRestartOnlyMaterializationOnLaterFailure()
    {
        PackageManager manager = CreatePackageManager(
            out _,
            out ExtensionProvider provider);
        await using var handlers = new ElementSourceHandlerRegistry([], provider);
        var package = new LocalPackage { Name = "CommittedMaterialization" };
        Element? retainedGraph = null;
        int metadataChanges = 0;
        handlers.Handlers.CollectionChanged += (_, _) => metadataChanges++;
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
        await Task.Delay(100);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(retainedGraph, Is.Not.Null);
            Assert.That(
                retainedGraph!.Objects.Single(),
                Is.TypeOf<MaterializedPackageObject>());
            Assert.That(handlers.Handlers, Is.Empty);
            Assert.That(metadataChanges, Is.EqualTo(2));
            Assert.That(MaterializingElementSourceHandlerExtension.UnloadCount, Is.Zero);
            Assert.That(manager.LoadedPackage, Is.Empty);
        }
        InvalidOperationException? retry = Assert.Throws<InvalidOperationException>(() =>
            manager.LoadExtensionsAndRegister(
                activity: null,
                package,
                assemblies: [],
                loadContext: null,
                [typeof(MaterializingElementSourceHandlerExtension)]));
        Assert.That(retry!.Message, Does.Contain("quarantined"));
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
            [typeof(LeaseManagedAiJobStatusResolverExtension)]);

        bool unloaded = await manager.Unload(package);

        Assert.Multiple(() =>
        {
            // No load context means nothing pins the package, so the unload succeeds and diagnostics stay idle.
            Assert.That(unloaded, Is.True);
            Assert.That(diagnostics.InvokeCount, Is.EqualTo(0));
        });
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
            [typeof(LeaseManagedAiJobStatusResolverExtension)]);
        ExtensionDescriptor descriptor = publicProvider
            .GetDescriptors<LeaseManagedAiJobStatusResolverExtension>()
            .Single();
        Assert.That(
            publicProvider.TryAcquire(
                descriptor.Id,
                out IExtensionLease<LeaseManagedAiJobStatusResolverExtension>? lease),
            Is.True);

        Task<bool> unload = manager.Unload(package).AsTask();
        try
        {
            await Task.Delay(100);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(unload.IsCompleted, Is.False);
                Assert.That(LeaseManagedAiJobStatusResolverExtension.UnloadCount, Is.Zero);
                Assert.That(publicProvider.Extensions, Is.Empty);
                Assert.That(
                    publicProvider.TryAcquire(
                        descriptor.Id,
                        out IExtensionLease<LeaseManagedAiJobStatusResolverExtension>? retiredLease),
                    Is.False);
                Assert.That(retiredLease, Is.Null);
            }
        }
        finally
        {
            lease!.Dispose();
        }

        Assert.That(await unload.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(LeaseManagedAiJobStatusResolverExtension.UnloadCount, Is.EqualTo(1));
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
            [typeof(LeaseManagedAiJobStatusResolverExtension)]);

        bool unloaded = await manager.Unload(package);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unloaded, Is.True);
            Assert.That(registry.SynchronizationCount, Is.GreaterThan(0));
            Assert.That(registry.GetExtensions<LeaseManagedAiJobStatusResolverExtension>(), Is.Empty);
            Assert.That(LeaseManagedAiJobStatusResolverExtension.UnloadCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Unload_DrainsIndependentPresentationAndCompletionLeases()
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        await using var results = new AiJobResultRegistry([], [], [], provider, null);
        var package = new LocalPackage { Name = "LiveAiResultPresentation" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [
                typeof(LeaseManagedAiJobPresentationExtension),
                typeof(LeaseManagedAiJobCompletionExtension),
            ]);

        Assert.That(results.TryAcquirePresenter(
            LeaseManagedAiJobPresentationExtension.Kind,
            out IAiJobPresenterLease? presentationLease), Is.True);
        Assert.That(results.TryAcquireCompletionPresenter(
            LeaseManagedAiJobCompletionExtension.Kind,
            out IAiJobCompletionPresenterLease? completionLease), Is.True);

        Task<bool> unload = manager.Unload(package).AsTask();
        await Task.Delay(100);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unload.IsCompleted, Is.False);
            Assert.That(LeaseManagedAiJobPresentationExtension.UnloadCount, Is.Zero);
            Assert.That(LeaseManagedAiJobCompletionExtension.UnloadCount, Is.Zero);
        }

        presentationLease!.Dispose();
        await Task.Delay(100);
        Assert.That(unload.IsCompleted, Is.False);
        completionLease!.Dispose();

        Assert.That(await unload.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LeaseManagedAiJobPresentationExtension.UnloadCount, Is.EqualTo(1));
            Assert.That(LeaseManagedAiJobCompletionExtension.UnloadCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Unload_DrainsLiveCaptionDescriptorAndPlacementLeases()
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], provider);
        var package = new LocalPackage { Name = "LiveCaptionPresentation" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [
                typeof(LeaseManagedCaptionTemplateDescriptorExtension),
                typeof(LeaseManagedCaptionPlacementExtension),
            ]);
        using ICaptionTemplateLease lease = catalog.Templates.Acquire(
            CaptionTemplateIds.DefaultText);

        Task<bool> unload = manager.Unload(package).AsTask();
        await Task.Delay(100);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unload.IsCompleted, Is.False);
            Assert.That(LeaseManagedCaptionTemplateDescriptorExtension.UnloadCount, Is.Zero);
            Assert.That(LeaseManagedCaptionPlacementExtension.UnloadCount, Is.Zero);
            Assert.That(
                catalog.Templates.GetRequired(CaptionTemplateIds.DefaultText).Name,
                Is.EqualTo("Default"));
        }

        lease.Dispose();
        Assert.That(await unload.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LeaseManagedCaptionTemplateDescriptorExtension.UnloadCount, Is.EqualTo(1));
            Assert.That(LeaseManagedCaptionPlacementExtension.UnloadCount, Is.EqualTo(1));
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
    public async Task Unload_RequiresRestartForElementSourceHandlerExtensions()
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        var package = new LocalPackage { Name = "MaterializedElementGraph" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(MaterializingElementSourceHandlerExtension)]);

        bool unloaded = await manager.Unload(package);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unloaded, Is.False);
            Assert.That(MaterializingElementSourceHandlerExtension.UnloadCount, Is.Zero);
            Assert.That(
                provider.GetExtensions<MaterializingElementSourceHandlerExtension>(),
                Has.Length.EqualTo(1));
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
            [typeof(BlockingCaptionDecoderExtension)]);

        Task<CaptionImportResult> decodeTask = Task.Run(() =>
            catalog.Serializer.Import("caption"u8, s_blockingCaptionFormat));
        Assert.That(
            BlockingCaptionDecoderExtension.WaitForDecode(TimeSpan.FromSeconds(5)),
            Is.True,
            "The test codec did not begin decoding.");

        Task<bool> unloadTask = Task.Run(async () => await manager.Unload(package));
        try
        {
            Assert.That(
                SpinWait.SpinUntil(
                    () => provider.GetExtensions<BlockingCaptionDecoderExtension>().Length == 0,
                    TimeSpan.FromSeconds(5)),
                Is.True,
                "Package removal did not begin.");
            Assert.Multiple(() =>
            {
                Assert.That(unloadTask.IsCompleted, Is.False);
                Assert.That(BlockingCaptionDecoderExtension.UnloadCount, Is.EqualTo(0));
            });
        }
        finally
        {
            BlockingCaptionDecoderExtension.ReleaseDecode();
        }

        CaptionImportResult decoded = await decodeTask.WaitAsync(TimeSpan.FromSeconds(5));
        bool unloaded = await unloadTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(decoded.IsSuccess, Is.True);
            Assert.That(unloaded, Is.True);
            Assert.That(BlockingCaptionDecoderExtension.UnloadCount, Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() =>
                catalog.Serializer.Import("after unload"u8, s_blockingCaptionFormat));
        });
    }

    [Test]
    public async Task Unload_RequiresRestartForCaptionElementFactoryExtensions()
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        var package = new LocalPackage { Name = "RestartOnlyCaptionTemplate" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(RestartOnlyCaptionElementFactoryExtension)]);

        bool unloaded = await manager.Unload(package);

        Assert.Multiple(() =>
        {
            Assert.That(unloaded, Is.False);
            Assert.That(RestartOnlyCaptionElementFactoryExtension.UnloadCount, Is.Zero);
            Assert.That(
                provider.GetExtensions<RestartOnlyCaptionElementFactoryExtension>(),
                Has.Length.EqualTo(1));
            Assert.That(manager.LoadedPackage, Does.Contain(package));
        });
    }

    [Test]
    public async Task Unload_RequiresRestartForAiJobResultApplicatorExtensions()
    {
        PackageManager manager = CreatePackageManager(out _, out ExtensionProvider provider);
        var package = new LocalPackage { Name = "PersistentAiResultGraph" };
        manager.LoadExtensionsAndRegister(
            activity: null,
            package,
            assemblies: [],
            loadContext: null,
            [typeof(RestartOnlyAiJobResultApplicatorExtension)]);

        bool unloaded = await manager.Unload(package);

        Assert.Multiple(() =>
        {
            Assert.That(unloaded, Is.False);
            Assert.That(RestartOnlyAiJobResultApplicatorExtension.UnloadCount, Is.Zero);
            Assert.That(
                provider.GetExtensions<RestartOnlyAiJobResultApplicatorExtension>(),
                Has.Length.EqualTo(1));
            Assert.That(manager.LoadedPackage, Does.Contain(package));
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

        public event EventHandler? ExtensionsChanged
        {
            add => _inner.ExtensionsChanged += value;
            remove => _inner.ExtensionsChanged -= value;
        }

        public IReadOnlyList<ExtensionDescriptor> Extensions
            => ((IExtensionProvider)_inner).Extensions;

        public IReadOnlyList<ExtensionDescriptor> GetDescriptors<TExtension>()
            where TExtension : Extension
            => ((IExtensionProvider)_inner).GetDescriptors<TExtension>();

        public bool TryAcquire<TExtension>(
            ExtensionId id,
            [NotNullWhen(true)] out IExtensionLease<TExtension>? lease)
            where TExtension : Extension
            => ((IExtensionProvider)_inner).TryAcquire(id, out lease);

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
    private sealed class FaultedDrainViewExtension : AiJobStatusResolverExtension
    {
        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<AiJobStatusResolverRegistration> Registrations { get; } =
        [
            new AiJobStatusResolverRegistration(
                new AiJobKindId("beutl.tests.faulted-drain"),
                new AiJobStatusMap([])),
        ];

        public static void Reset() => UnloadCount = 0;

        public override void Unload() => UnloadCount++;
    }

    [Export]
    private sealed class LeaseManagedAiJobStatusResolverExtension : AiJobStatusResolverExtension
    {
        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<AiJobStatusResolverRegistration> Registrations { get; } =
        [
            new AiJobStatusResolverRegistration(
                new AiJobKindId("beutl.tests.lease-managed"),
                new AiJobStatusMap([])),
        ];

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

    [Export]
    private sealed class BlockingCaptionDecoderExtension : CaptionDecoderExtension, ICaptionDecoder
    {
        private static readonly ManualResetEventSlim s_decodeStarted = new();
        private static readonly ManualResetEventSlim s_releaseDecode = new();
        private readonly IReadOnlyCollection<CaptionDecoderRegistration> _registrations;

        public BlockingCaptionDecoderExtension()
        {
            _registrations =
            [
                new CaptionDecoderRegistration(s_blockingCaptionFormat, this),
            ];
        }

        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<CaptionDecoderRegistration> Registrations
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
    private sealed class LeaseManagedCaptionTemplateDescriptorExtension :
        CaptionTemplateDescriptorExtension
    {
        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<CaptionTemplateDescriptorRegistration> Registrations
        { get; } =
        [
            new CaptionTemplateDescriptorRegistration(
                new CaptionTemplateDescriptor(
                    CaptionTemplateIds.DefaultText,
                    new CaptionTemplateProviderId("beutl.tests"),
                    "Replacement"),
                CaptionTemplateRegistrationMode.Replace),
        ];

        public override void Unload() => UnloadCount++;

        public static void Reset() => UnloadCount = 0;
    }

    [Export]
    private sealed class LeaseManagedCaptionPlacementExtension :
        CaptionPlacementPolicyExtension,
        ICaptionPlacementPolicy
    {
        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<CaptionPlacementPolicyRegistration> Registrations
        { get; }

        public LeaseManagedCaptionPlacementExtension()
        {
            Registrations =
            [
                new CaptionPlacementPolicyRegistration(
                    CaptionTemplateIds.DefaultText,
                    this,
                    CaptionTemplateRegistrationMode.Replace),
            ];
        }

        public CaptionElementPlacement Place(
            CaptionCue cue,
            CaptionElementContext context,
            CaptionElementPlacement placement,
            int elementIndex)
            => placement with { Position = new Beutl.Graphics.Point(12, 34) };

        public override void Unload() => UnloadCount++;

        public static void Reset() => UnloadCount = 0;
    }

    [Export]
    private sealed class RestartOnlyCaptionElementFactoryExtension :
        CaptionElementFactoryExtension,
        ICaptionElementFactory
    {
        private readonly IReadOnlyCollection<CaptionElementFactoryRegistration> _registrations;

        public RestartOnlyCaptionElementFactoryExtension()
        {
            _registrations =
            [
                new CaptionElementFactoryRegistration(
                    s_restartOnlyCaptionTemplate,
                    this),
            ];
        }

        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<CaptionElementFactoryRegistration> Registrations
            => _registrations;

        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            =>
            [
                context.CreateDescription(
                    cue,
                    () => new TextBlock { Text = { CurrentValue = cue.Text } }),
            ];

        public override void Unload()
        {
            UnloadCount++;
        }

        public static void Reset()
        {
            UnloadCount = 0;
        }
    }

    [Export]
    private sealed class LeaseManagedAiJobPresentationExtension :
        AiJobPresentationExtension,
        IAiJobPresenter
    {
        public static AiJobKindId Kind { get; } = new("beutl.tests.presentation");

        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<AiJobPresentationRegistration> Registrations { get; }

        public LeaseManagedAiJobPresentationExtension()
        {
            Registrations = [new AiJobPresentationRegistration(Kind, this)];
        }

        public AiJobPresentation Present(AiJob job, AiJobStatusSemantics status)
            => new("Test", "Ready", "Test", string.Empty, false);

        public override void Unload() => UnloadCount++;

        public static void Reset() => UnloadCount = 0;
    }

    [Export]
    private sealed class LeaseManagedAiJobCompletionExtension :
        AiJobCompletionExtension,
        IAiJobCompletionPresenter
    {
        public static AiJobKindId Kind { get; } = new("beutl.tests.completion");

        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<AiJobCompletionRegistration> Registrations { get; }

        public LeaseManagedAiJobCompletionExtension()
        {
            Registrations = [new AiJobCompletionRegistration(Kind, this)];
        }

        public AiJobCompletionPresentation? CreateCompletion(
            AiJob job,
            AiJobStatusSemantics status)
            => null;

        public override void Unload() => UnloadCount++;

        public static void Reset() => UnloadCount = 0;
    }

    [Export]
    private sealed class RestartOnlyAiJobResultApplicatorExtension : AiJobResultApplicatorExtension
    {
        public static int UnloadCount { get; private set; }

        public override IReadOnlyCollection<AiJobResultApplicatorRegistration> Registrations { get; } = [];

        public override void Unload()
        {
            UnloadCount++;
        }

        public static void Reset()
        {
            UnloadCount = 0;
        }
    }
}

public sealed partial class MaterializedPackageObject : EngineObject;
