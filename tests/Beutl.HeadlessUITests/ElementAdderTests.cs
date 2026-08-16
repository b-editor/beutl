using System.Collections.Specialized;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ElementAdderTests
{
    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();

        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        return (EditViewModel)tab.Context.Value;
    }

    [AvaloniaTest]
    public async Task AddAsync_CommitsOneBatchAndUndoRedoRestoresEveryElement()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-history");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        int initialUndoCount = editor.HistoryManager.UndoCount;

        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape())),
            new ElementDescription(
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new EllipseShape())),
        ], CancellationToken.None);
        HeadlessTestHelpers.Settle();

        IReadOnlyList<Element> created = result.Elements;
        Guid[] createdIds = created.Select(element => element.Id).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Items, Has.Count.EqualTo(2));
            Assert.That(editor.Scene.Children.Select(element => element.Id), Is.EqualTo(createdIds));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(initialUndoCount + 1));
        }

        Assert.That(editor.HistoryManager.Undo(), Is.True);
        HeadlessTestHelpers.Settle();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(initialUndoCount));
        }

        Assert.That(editor.HistoryManager.Redo(), Is.True);
        HeadlessTestHelpers.Settle();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Scene.Children.Select(element => element.Id), Is.EqualTo(createdIds));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(initialUndoCount + 1));
        }
    }

    [AvaloniaTest]
    public async Task AddAsync_WhenFactoryFails_AddsNothingAndStagesNoFiles()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-factory-failure");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        string sceneDirectory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
        string[] filesBefore = Directory.GetFiles(sceneDirectory, "*.belm", SearchOption.AllDirectories);

        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape())),
            new ElementDescription(
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(
                    static () => throw new InvalidOperationException("Factory failure"))),
        ], CancellationToken.None);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure, Is.TypeOf<ElementMaterializationFailure>());
            Assert.That(result.Failure!.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(result.Elements, Is.Empty);
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.Scene.Groups, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
            Assert.That(
                Directory.GetFiles(sceneDirectory, "*.belm", SearchOption.AllDirectories),
                Is.EqualTo(filesBefore));
        }
    }

    [AvaloniaTest]
    public async Task AddAsync_EmptyAndUnsupportedRequestsReturnDistinctFailures()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-preflight-failures");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;

        ElementAddResult empty = await adder.AddAsync([], CancellationToken.None);
        ElementAddResult unsupported = await adder.AddAsync(
        [
            new ElementDescription(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                0,
                new ElementSource.File("/tmp/unsupported.beutl-test")),
        ], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.Failure, Is.TypeOf<EmptyElementAddRequestFailure>());
            Assert.That(unsupported.Failure, Is.TypeOf<UnsupportedElementSourceFailure>());
            Assert.That(
                (unsupported.FailedDescription?.Source as ElementSource.File)?.FileName,
                Does.EndWith("unsupported.beutl-test"));
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task AddAsync_WhenTargetLayerIsLocked_RefusesEntireBatchBeforeFactoryRuns()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-locked-layer");
        using (editor.HistoryManager.SuppressRecording())
        {
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });
        }

        bool factoryCalled = false;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape())),
            new ElementDescription(
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(2),
                Layer: 2,
                Source: new ElementSource.EngineObject(() =>
                {
                    factoryCalled = true;
                    return new EllipseShape();
                })),
        ], CancellationToken.None);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failure, Is.TypeOf<LockedElementLayerFailure>());
            Assert.That(((LockedElementLayerFailure)result.Failure!).Layer, Is.EqualTo(2));
            Assert.That(result.Elements, Is.Empty);
            Assert.That(factoryCalled, Is.False);
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task ElementTemplateResolver_AddsRegeneratedCompleteElementThroughBatchPipeline()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-template");
        var sourceShape = new RectShape();
        var source = new Element
        {
            Start = TimeSpan.FromSeconds(20),
            Length = TimeSpan.FromSeconds(7),
            ZIndex = 9,
            Name = "Authored template",
        };
        source.AddObject(sourceShape);
        ObjectTemplateItem template = ObjectTemplateItem.CreateFromInstance(source, "Template");
        ElementDescription description = ElementTemplateResolver.CreateDescription(
            template,
            TimeSpan.FromSeconds(3),
            4);
        ElementDescription zeroLengthDescription = ElementTemplateResolver.CreateDescription(
            template,
            TimeSpan.FromSeconds(11),
            5,
            lengthOverride: TimeSpan.Zero);
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;

        ElementAddResult result = await adder.AddAsync(
            [description, zeroLengthDescription],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();

        Element created = result.Items[0].PrimaryElement;
        Element zeroLengthElement = result.Items[1].PrimaryElement;
        RectShape createdShape = created.Objects.OfType<RectShape>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(description.Length, Is.Null);
            Assert.That(created.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(created.Length, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(created.ZIndex, Is.EqualTo(4));
            Assert.That(created.Name, Is.EqualTo("Authored template"));
            Assert.That(created.Id, Is.Not.EqualTo(source.Id));
            Assert.That(createdShape.Id, Is.Not.EqualTo(sourceShape.Id));
            Assert.That(File.Exists(created.Uri!.LocalPath), Is.True);
            Assert.That(zeroLengthDescription.Length, Is.EqualTo(TimeSpan.Zero));
            Assert.That(zeroLengthElement.Length, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [AvaloniaTest]
    public async Task RegisteredAsyncHandler_MaterializesGroupedElementsAndUnregistersCleanly()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-plugin-handler");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new GroupedTestSourceHandler();
        int initialUndoCount = editor.HistoryManager.UndoCount;
        var description = new ElementDescription(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3),
            4,
            new TestElementSource("plugin"));

        IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));
        ElementAddResult result = await adder.AddAsync([description], CancellationToken.None);
        await registration.DisposeAsync();
        ElementAddResult afterUnregister = await adder.AddAsync([description], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Elements, Has.Count.EqualTo(2));
            Assert.That(result.Elements.Select(element => element.ZIndex), Is.EqualTo(new[] { 4, 5 }));
            Assert.That(editor.Scene.Groups, Has.Count.EqualTo(1));
            Assert.That(editor.Scene.Groups.Single(), Is.EquivalentTo(result.Elements.Select(element => element.Id)));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(initialUndoCount + 1));
            Assert.That(handler.PreflightCount, Is.EqualTo(1));
            Assert.That(handler.MaterializationCount, Is.EqualTo(1));
            Assert.That(handler.LastPreflight!.IsDisposed, Is.True);
            Assert.That(handler.Resource.IsDisposed, Is.True);
            Assert.That(afterUnregister.Failure, Is.TypeOf<UnsupportedElementSourceFailure>());
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_CanReturnExtensibleTypedPreflightFailure()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-plugin-failure");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var failure = new TestElementAddFailure("The plugin rejected this source.");
        var handler = new RejectingTestSourceHandler(failure);
        await using IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));

        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                0,
                new TestElementSource("rejected")),
        ], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure, Is.SameAs(failure));
            Assert.That(result.Failure!.Id, Is.EqualTo(new ElementAddFailureId("example.test.rejected")));
            Assert.That(handler.MaterializationCalled, Is.False);
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task CancellationDuringRegisteredPreflight_LeavesPersistenceAndHistoryUntouched()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-plugin-cancellation");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new BlockingTestSourceHandler();
        await using IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));
        using var cancellationTokenSource = new CancellationTokenSource();
        ValueTask<ElementAddResult> operation = adder.AddAsync(
        [
            new ElementDescription(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                0,
                new TestElementSource("cancel")),
        ], cancellationTokenSource.Token);
        await handler.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellationTokenSource.Cancel();

        bool canceled = false;
        try
        {
            await operation;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(canceled, Is.True);
            Assert.That(handler.MaterializationCalled, Is.False);
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_DisposalWaitsForPreflightAndMaterialization()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-handler-lease");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new LeaseBlockingTestSourceHandler();
        IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));
        Task<ElementAddResult> operation = adder.AddAsync(
            [CreateTestDescription("lease", 0)],
            CancellationToken.None).AsTask();
        Task? disposeTask = null;

        try
        {
            await handler.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            disposeTask = registration.DisposeAsync().AsTask();
            await WaitUntilAsync(IsHandlerRetired, TimeSpan.FromSeconds(5));
            Assert.That(disposeTask.IsCompleted, Is.False);

            handler.ReleasePreflight.TrySetResult();
            await handler.MaterializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(disposeTask.IsCompleted, Is.False);

            handler.ReleaseMaterialization.TrySetResult();
            ElementAddResult result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
            }
        }
        finally
        {
            handler.ReleasePreflight.TrySetResult();
            handler.ReleaseMaterialization.TrySetResult();
            Task finalDisposeTask = disposeTask ?? registration.DisposeAsync().AsTask();
            try
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                await finalDisposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        bool IsHandlerRetired()
        {
            if (!adder.SourceHandlers.TryAcquire(
                    typeof(TestElementSource),
                    out IElementSourceHandlerLease? candidate))
            {
                return true;
            }

            candidate!.Dispose();
            return false;
        }
    }

    [AvaloniaTest]
    public async Task EditorDispose_OnUiThreadCancelsAwaitingHandlerAndDrainsItsLease()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-dispose-during-await");
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new BlockingTestSourceHandler();
        _ = adder.SourceHandlers.Register(new ElementSourceHandlerRegistration(handler));
        Task<ElementAddResult> operation = adder.AddAsync(
            [CreateTestDescription("dispose", 0)],
            CancellationToken.None).AsTask();
        await handler.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task closeTask = TestShell.Editor.CloseTabItem(tab).AsTask();

        Assert.That(closeTask.IsCompleted, Is.False);
        OperationCanceledException? cancellation = null;
        try
        {
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException ex)
        {
            cancellation = ex;
        }
        await closeTask.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancellation, Is.Not.Null);
            Assert.That(handler.MaterializationCalled, Is.False);
            Assert.That(TestShell.Editor.TabItems, Does.Not.Contain(tab));
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_LockedLayerReleasesPreflightLeaseBeforeReturningFailure()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-plugin-locked-lease");
        using (editor.HistoryManager.SuppressRecording())
        {
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = 5, IsLocked = true });
        }
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new GroupedTestSourceHandler();
        await using IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));

        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                4,
                new TestElementSource("locked")),
        ], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure, Is.TypeOf<LockedElementLayerFailure>());
            Assert.That(handler.LastPreflight, Is.Not.Null);
            Assert.That(handler.LastPreflight!.IsDisposed, Is.True);
            Assert.That(handler.MaterializationCount, Is.Zero);
            Assert.That(handler.Resource.IsDisposed, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task MaterializationResource_IsReleasedAfterSuccessAndRollback()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-transfer-ownership");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var successfulHandler = new OwnershipTestSourceHandler();
        await using (IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
                         new ElementSourceHandlerRegistration(successfulHandler)))
        {
            ElementAddResult success = await adder.AddAsync(
            [
                new ElementDescription(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1),
                    0,
                    new TestElementSource("commit")),
            ], CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(success.IsSuccess, Is.True);
                Assert.That(successfulHandler.Preflight!.IsDisposed, Is.True);
                Assert.That(successfulHandler.Resource.IsDisposed, Is.True);
            }
        }

        var rollbackHandler = new OwnershipTestSourceHandler();
        await using IElementSourceHandlerRegistration rollbackRegistration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(rollbackHandler));
        NotifyCollectionChangedEventHandler mutationFailure = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
                throw new InvalidOperationException("Injected mutation failure");
        };
        editor.Scene.Children.CollectionChanged += mutationFailure;
        ElementAddResult rollback;
        try
        {
            rollback = await adder.AddAsync(
            [
                new ElementDescription(
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(1),
                    1,
                    new TestElementSource("rollback")),
            ], CancellationToken.None);
        }
        finally
        {
            editor.Scene.Children.CollectionChanged -= mutationFailure;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rollback.Failure, Is.TypeOf<ElementSceneMutationFailure>());
            Assert.That(rollbackHandler.Preflight!.IsDisposed, Is.True);
            Assert.That(rollbackHandler.Resource.IsDisposed, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task ExtensionHandler_IsComposedForOpenEditorAndRetiredOnPackageRemoval()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-extension-composition");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        const int packageId = 98_765_432;
        var extension = new TestSourceHandlerExtension(new GroupedTestSourceHandler());

        editor.ExtensionProvider.AddExtensions(packageId, [extension]);
        try
        {
            ElementAddResult result = await adder.AddAsync(
            [
                new ElementDescription(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1),
                    0,
                    new TestElementSource("extension")),
            ], CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
        }
        finally
        {
            editor.ExtensionProvider.RemoveExtensions(packageId);
        }

        ElementAddResult afterRemoval = await adder.AddAsync(
        [
            new ElementDescription(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(1),
                0,
                new TestElementSource("extension-removed")),
        ], CancellationToken.None);

        Assert.That(afterRemoval.Failure, Is.TypeOf<UnsupportedElementSourceFailure>());
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_RejectsDuplicateElementIdsAcrossTheBatchAndScene()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-duplicate-ids");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new FixedIdSourceHandler();
        await using IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));

        ElementAddResult duplicateBatch = await adder.AddAsync(
        [
            CreateTestDescription("first", 0),
            CreateTestDescription("second", 1),
        ], CancellationToken.None);
        ElementAddResult first = await adder.AddAsync(
            [CreateTestDescription("committed", 0)],
            CancellationToken.None);
        ElementAddResult duplicateScene = await adder.AddAsync(
            [CreateTestDescription("duplicate", 1)],
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(duplicateBatch.Failure, Is.TypeOf<InvalidElementMaterializationFailure>());
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(duplicateScene.Failure, Is.TypeOf<InvalidElementMaterializationFailure>());
            Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
            Assert.That(editor.Scene.Children[0].Id, Is.EqualTo(handler.FixedId));
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_RevalidatesLayerLockAfterAsyncMaterialization()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-late-layer-lock");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new MutationTestSourceHandler((context, _) =>
        {
            using (editor.HistoryManager.SuppressRecording())
            {
                context.Scene.Layers.Add(new TimelineLayer
                {
                    ZIndex = context.Description.Layer,
                    IsLocked = true,
                });
            }
        });
        await using IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));

        ElementAddResult result = await adder.AddAsync(
            [CreateTestDescription("late-lock", 3)],
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure, Is.TypeOf<LockedElementLayerFailure>());
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_RevalidatesSceneIdentityAfterAsyncMaterialization()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-late-scene-change");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        Guid originalId = editor.Scene.Id;
        Guid changedId = Guid.NewGuid();
        var handler = new MutationTestSourceHandler((context, _) =>
        {
            using (editor.HistoryManager.SuppressRecording())
            {
                context.Scene.Id = changedId;
            }
        });
        await using IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));

        ElementAddResult result;
        try
        {
            result = await adder.AddAsync(
                [CreateTestDescription("scene-change", 0)],
                CancellationToken.None);
        }
        finally
        {
            using (editor.HistoryManager.SuppressRecording())
            {
                editor.Scene.Id = originalId;
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure, Is.TypeOf<ElementSceneChangedFailure>());
            Assert.That(((ElementSceneChangedFailure)result.Failure!).ExpectedSceneId, Is.EqualTo(originalId));
            Assert.That(((ElementSceneChangedFailure)result.Failure!).ActualSceneId, Is.EqualTo(changedId));
            Assert.That(editor.Scene.Children, Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_RevalidatesIdentifierUniquenessAfterAsyncMaterialization()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-late-id-conflict");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        Element? competing = null;
        var handler = new MutationTestSourceHandler((context, materialized) =>
        {
            competing = new Element
            {
                Start = TimeSpan.FromSeconds(10),
                Length = TimeSpan.FromSeconds(1),
                ZIndex = 10,
                Uri = new Uri(Path.Combine(
                    Path.GetDirectoryName(context.Scene.Uri!.LocalPath)!,
                    $"competing-{Guid.NewGuid():N}.belm")),
            };
            using (editor.HistoryManager.SuppressRecording())
            {
                context.Scene.AddChild(competing);
            }
            materialized.Id = competing.Id;
        });
        await using IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));

        ElementAddResult result = await adder.AddAsync(
            [CreateTestDescription("id-conflict", 0)],
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure, Is.TypeOf<InvalidElementMaterializationFailure>());
            Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
            Assert.That(editor.Scene.Children[0], Is.SameAs(competing));
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task ResourceReleaseFailure_DoesNotTurnACommittedBatchIntoAFailure()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-release-failure");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new ThrowingDisposeSourceHandler();
        await using IElementSourceHandlerRegistration registration = adder.SourceHandlers.Register(
            new ElementSourceHandlerRegistration(handler));

        ElementAddResult result = await adder.AddAsync(
            [CreateTestDescription("release", 0)],
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
            Assert.That(handler.Preflight.DisposeAttempts, Is.EqualTo(1));
            Assert.That(handler.Resource.DisposeAttempts, Is.EqualTo(1));
        }
    }

    private static ElementDescription CreateTestDescription(string value, int layer)
        => new(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            layer,
            new TestElementSource(value));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        const int DelayMilliseconds = 10;
        int attempts = (int)Math.Ceiling(timeout.TotalMilliseconds / DelayMilliseconds);
        for (int index = 0; index < attempts; index++)
        {
            if (condition())
                return;

            await Task.Delay(DelayMilliseconds);
        }

        Assert.Fail("The expected handler state was not reached before the timeout.");
    }

    [AvaloniaTest]
    public async Task AddAsync_WhenSceneMutationFails_RollsBackBatchAndCleansStagedFiles()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("element-adder-mutation-failure");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        string sceneDirectory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
        string[] filesBefore = Directory.GetFiles(sceneDirectory, "*.belm", SearchOption.AllDirectories);
        bool allFilesWereStaged = false;
        bool shouldThrow = true;
        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add && editor.Scene.Children.Count == 1)
            {
                allFilesWereStaged = Directory.GetFiles(
                    sceneDirectory,
                    "*.belm",
                    SearchOption.AllDirectories).Length == filesBefore.Length + 2;
            }

            if (args.Action == NotifyCollectionChangedAction.Add
                && editor.Scene.Children.Count == 2
                && shouldThrow)
            {
                shouldThrow = false;
                throw new InvalidOperationException("Injected collection mutation failure");
            }
        };
        editor.Scene.Children.CollectionChanged += handler;

        try
        {
            ElementAddResult result = await adder.AddAsync(
            [
                new ElementDescription(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    Layer: 0,
                    Source: new ElementSource.EngineObject(() => new RectShape())),
                new ElementDescription(
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(2),
                    Layer: 0,
                    Source: new ElementSource.EngineObject(() => new EllipseShape())),
            ], CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(result.Failure, Is.TypeOf<ElementSceneMutationFailure>());
                Assert.That(result.Failure!.Exception, Is.TypeOf<InvalidOperationException>());
            });
        }
        finally
        {
            editor.Scene.Children.CollectionChanged -= handler;
        }
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allFilesWereStaged, Is.True);
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.Scene.Groups, Is.Empty);
            Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
            Assert.That(
                Directory.GetFiles(sceneDirectory, "*.belm", SearchOption.AllDirectories),
                Is.EqualTo(filesBefore));
        }
    }

    private sealed record TestElementSource(string Value) : ElementSource;

    private sealed class TestPreflight(int primaryLayer) : IElementSourcePreflight
    {
        public int PrimaryLayer { get; } = primaryLayer;

        public bool IsDisposed { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            IsDisposed = true;
        }
    }

    private sealed record TestElementAddFailure : ElementAddFailure
    {
        public TestElementAddFailure(string message)
            : base(new ElementAddFailureId("example.test.rejected"), message)
        {
        }
    }

    private sealed class GroupedTestSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(TestElementSource);

        public int PreflightCount { get; private set; }

        public int MaterializationCount { get; private set; }

        public TrackingAsyncDisposable Resource { get; } = new();

        public TestPreflight? LastPreflight { get; private set; }

        public async ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            PreflightCount++;
            LastPreflight = new TestPreflight(context.Description.Layer);
            return ElementSourcePreflightResult.Ready(
                LastPreflight,
                [context.Description.Layer, context.Description.Layer + 1]);
        }

        public async ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            MaterializationCount++;
            var state = (TestPreflight)preflight;
            var primary = new Element
            {
                Start = context.Description.Start,
                Length = context.Description.Length!.Value,
                ZIndex = state.PrimaryLayer,
            };
            var companion = new Element
            {
                Start = context.Description.Start,
                Length = context.Description.Length.Value,
                ZIndex = state.PrimaryLayer + 1,
            };
            IReadOnlySet<Guid> group = new HashSet<Guid> { primary.Id, companion.Id };
            return ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(
                    primary,
                    [companion],
                    [group],
                    [ElementMaterializationResource.TemporaryAsync(Resource)]));
        }
    }

    private sealed class RejectingTestSourceHandler(TestElementAddFailure failure)
        : IElementSourceHandler
    {
        public Type SourceType => typeof(TestElementSource);

        public bool MaterializationCalled { get; private set; }

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ElementSourcePreflightResult.Rejected(failure));
        }

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            MaterializationCalled = true;
            throw new AssertionException("Rejected preflight must not be materialized.");
        }
    }

    private sealed class BlockingTestSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(TestElementSource);

        public TaskCompletionSource PreflightStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool MaterializationCalled { get; private set; }

        public async ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            PreflightStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new AssertionException("Canceled preflight unexpectedly resumed.");
        }

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            MaterializationCalled = true;
            throw new AssertionException("Canceled preflight must not be materialized.");
        }
    }

    private sealed class LeaseBlockingTestSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(TestElementSource);

        public TaskCompletionSource PreflightStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleasePreflight { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource MaterializationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseMaterialization { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            PreflightStarted.TrySetResult();
            await ReleasePreflight.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return ElementSourcePreflightResult.Ready(
                new TestPreflight(context.Description.Layer),
                [context.Description.Layer]);
        }

        public async ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            MaterializationStarted.TrySetResult();
            await ReleaseMaterialization.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var element = new Element
            {
                Start = context.Description.Start,
                Length = context.Description.Length!.Value,
                ZIndex = context.Description.Layer,
            };
            return ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(element));
        }
    }

    private sealed class OwnershipTestSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(TestElementSource);

        public TestPreflight? Preflight { get; private set; }

        public TrackingDisposable Resource { get; } = new();

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Preflight = new TestPreflight(context.Description.Layer);
            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
                Preflight,
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
                Length = context.Description.Length!.Value,
                ZIndex = context.Description.Layer,
            };
            return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(
                    element,
                    resources: [ElementMaterializationResource.Temporary(Resource)])));
        }
    }

    private sealed class TestSourceHandlerExtension(IElementSourceHandler handler)
        : ElementSourceHandlerExtension
    {
        public override IReadOnlyCollection<ElementSourceHandlerRegistration> Registrations { get; } =
        [
            new ElementSourceHandlerRegistration(handler),
        ];
    }

    private sealed class FixedIdSourceHandler : IElementSourceHandler
    {
        public Guid FixedId { get; } = Guid.NewGuid();

        public Type SourceType => typeof(TestElementSource);

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
                new TestPreflight(context.Description.Layer),
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
                Id = FixedId,
                Start = context.Description.Start,
                Length = context.Description.Length!.Value,
                ZIndex = context.Description.Layer,
            };
            return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(element)));
        }
    }

    private sealed class MutationTestSourceHandler(
        Action<ElementSourceMaterializationContext, Element> mutate) : IElementSourceHandler
    {
        public Type SourceType => typeof(TestElementSource);

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
                new TestPreflight(context.Description.Layer),
                [context.Description.Layer]));
        }

        public async ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var element = new Element
            {
                Start = context.Description.Start,
                Length = context.Description.Length!.Value,
                ZIndex = context.Description.Layer,
            };
            mutate(context, element);
            return ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(element));
        }
    }

    private sealed class ThrowingDisposeSourceHandler : IElementSourceHandler
    {
        public ThrowingPreflight Preflight { get; } = new();

        public ThrowingAsyncDisposable Resource { get; } = new();

        public Type SourceType => typeof(TestElementSource);

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
                Preflight,
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
                Length = context.Description.Length!.Value,
                ZIndex = context.Description.Layer,
            };
            return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(
                    element,
                    resources: [ElementMaterializationResource.TemporaryAsync(Resource)])));
        }
    }

    private sealed class ThrowingPreflight : IElementSourcePreflight
    {
        public int DisposeAttempts { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAttempts++;
            throw new InvalidOperationException("Injected preflight disposal failure");
        }
    }

    private sealed class ThrowingAsyncDisposable : IAsyncDisposable
    {
        public int DisposeAttempts { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAttempts++;
            throw new InvalidOperationException("Injected resource disposal failure");
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            IsDisposed = true;
        }
    }
}
