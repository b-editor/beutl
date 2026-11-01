using System.Collections.Specialized;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ElementAdderTests
{
    [AvaloniaTest]
    public async Task AddAsync_CommitsOneBatchAndUndoRedoRestoresEveryElement()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-history");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        int initialUndoCount = editor.HistoryManager.UndoCount;

        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.Zero,
                MediaTime.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape())),
            new ElementDescription(
                MediaTime.FromSeconds(3),
                MediaTime.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new EllipseShape())),
        ], CancellationToken.None);
        HeadlessTestHelpers.Settle();

        IReadOnlyList<Element> created = result.Elements;
        Guid[] createdIds = created.Select(element => element.Id.Value).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Items, Has.Count.EqualTo(2));
            Assert.That(created[0].AccentColor.Value, Is.EqualTo(Color.Parse("#ffc9ba6f")));
            Assert.That(created[1].AccentColor.Value, Is.EqualTo(Color.Parse("#ffaac75a")));
            Assert.That(editor.Scene.Children.Select(element => element.Id.Value), Is.EqualTo(createdIds));
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
            Assert.That(editor.Scene.Children.Select(element => element.Id.Value), Is.EqualTo(createdIds));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(initialUndoCount + 1));
        }
    }

    [AvaloniaTest]
    public async Task AddAsync_WhenFactoryFails_AddsNothingAndStagesNoFiles()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-factory-failure");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        string sceneDirectory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
        string[] filesBefore = Directory.GetFiles(sceneDirectory, "*.belm", SearchOption.AllDirectories);

        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.Zero,
                MediaTime.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape())),
            new ElementDescription(
                MediaTime.FromSeconds(3),
                MediaTime.FromSeconds(2),
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
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-preflight-failures");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;

        ElementAddResult empty = await adder.AddAsync([], CancellationToken.None);
        ElementAddResult unsupported = await adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.Zero,
                MediaTime.FromSeconds(1),
                0,
                new ElementSource.File("/tmp/unsupported.beutl-test")),
        ], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.Failure, Is.TypeOf<EmptyElementAddRequestFailure>());
            Assert.That(unsupported.Failure, Is.TypeOf<UnsupportedElementSourceFailure>());
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task AddAsync_WhenTargetLayerIsLocked_RefusesEntireBatchBeforeFactoryRuns()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-locked-layer");
        using (editor.HistoryManager.SuppressRecording())
        {
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, IsLocked = { Value = true } });
        }

        bool factoryCalled = false;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.Zero,
                MediaTime.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape())),
            new ElementDescription(
                MediaTime.FromSeconds(3),
                MediaTime.FromSeconds(2),
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
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-template");
        var sourceShape = new RectShape();
        var source = new Element
        {
            Start = { Value = MediaTime.FromSeconds(20) },
            Length = { Value = MediaTime.FromSeconds(7) },
            ZIndex = { Value = 9 },
            Name = { Value = "Authored template" },
        };
        source.AddObject(sourceShape);
        ObjectTemplateItem template = ObjectTemplateItem.CreateFromInstance(source, "Template");
        ElementDescription description = ElementTemplateResolver.CreateDescription(
            template,
            MediaTime.FromSeconds(3),
            4);
        ElementDescription zeroLengthDescription = ElementTemplateResolver.CreateDescription(
            template,
            MediaTime.FromSeconds(11),
            5,
            lengthOverride: MediaTime.Zero);
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
            Assert.That(created.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(created.Length.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
            Assert.That(created.ZIndex.Value, Is.EqualTo(4));
            Assert.That(created.Name.Value, Is.EqualTo("Authored template"));
            Assert.That(created.Id.Value, Is.Not.EqualTo(source.Id.Value));
            Assert.That(createdShape.Id.Value, Is.Not.EqualTo(sourceShape.Id.Value));
            Assert.That(zeroLengthDescription.Length, Is.EqualTo(MediaTime.Zero));
            Assert.That(zeroLengthElement.Length.Value, Is.EqualTo(MediaTime.Zero));
        }
    }

    [AvaloniaTest]
    public async Task ElementSetTemplate_AddsItsElementsTogetherWithTheirGroupAsOneStep()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-set-template");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ObjectTemplateItem template = CreateSetTemplate();
        int initialUndoCount = editor.HistoryManager.UndoCount;

        ElementAddResult first = await adder.AddAsync(
            [ElementTemplateResolver.CreateDescription(template, MediaTime.FromSeconds(10), 3)],
            CancellationToken.None);
        // The place asked for is taken now, so the second copy goes to a free one, still in one piece.
        ElementAddResult second = await adder.AddAsync(
            [ElementTemplateResolver.CreateDescription(template, MediaTime.FromSeconds(10), 3)],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(
                first.Elements.Select(e => (e.Name.Value, e.Start.Value, e.Length.Value, e.ZIndex.Value)),
                Is.EquivalentTo(new[]
                {
                    ("Back", MediaTime.FromSeconds(10), MediaTime.FromSeconds(4), 3),
                    ("Text", MediaTime.FromSeconds(11), MediaTime.FromSeconds(2), 5),
                }));
            Assert.That(first.Items.Single().PrimaryElement.Name.Value, Is.EqualTo("Back"));
            Assert.That(Offsets(second.Elements), Is.EquivalentTo(Offsets(first.Elements)));
            Assert.That(
                second.Elements.Any(e => first.Elements.Any(
                    f => f.ZIndex.Value == e.ZIndex.Value && f.Range.Intersects(e.Range))),
                Is.False);
            Assert.That(editor.Scene.Groups, Has.Count.EqualTo(2));
            Assert.That(editor.Scene.Groups.Count(g => g.SetEquals(first.Elements.Select(e => e.Id.Value))), Is.EqualTo(1));
            Assert.That(editor.Scene.Groups.Count(g => g.SetEquals(second.Elements.Select(e => e.Id.Value))), Is.EqualTo(1));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(initialUndoCount + 2));
        }

        Assert.That(editor.HistoryManager.Undo(), Is.True);
        HeadlessTestHelpers.Settle();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Scene.Children, Is.EquivalentTo(first.Elements));
            Assert.That(editor.Scene.Groups, Has.Count.EqualTo(1));
        }
    }

    [AvaloniaTest]
    public async Task ElementSetTemplate_RefusesALockedLayerAndAnExplicitLength()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-set-template-refused");
        using (editor.HistoryManager.SuppressRecording())
        {
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, IsLocked = { Value = true } });
        }

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ObjectTemplateItem template = CreateSetTemplate();

        ElementAddResult locked = await adder.AddAsync(
            [ElementTemplateResolver.CreateDescription(template, MediaTime.Zero, 2)],
            CancellationToken.None);
        ElementAddResult lengthened = await adder.AddAsync(
            [ElementTemplateResolver.CreateDescription(template, MediaTime.Zero, 0, MediaTime.FromSeconds(1))],
            CancellationToken.None);
        // Its second element would land on the locked layer, so the set goes where neither does.
        ElementAddResult moved = await adder.AddAsync(
            [ElementTemplateResolver.CreateDescription(template, MediaTime.Zero, 0)],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(locked.Failure, Is.TypeOf<LockedElementLayerFailure>());
            Assert.That(lengthened.Failure, Is.TypeOf<ElementSourcePreflightFailure>());
            Assert.That(moved.IsSuccess, Is.True);
            Assert.That(moved.Elements.Select(e => e.ZIndex.Value), Has.None.EqualTo(2));
            Assert.That(
                Offsets(moved.Elements),
                Is.EquivalentTo(new[] { (MediaTime.Zero, 0), (MediaTime.FromSeconds(1), 2) }));
            Assert.That(editor.Scene.Children, Is.EquivalentTo(moved.Elements));
        }
    }

    // A "Back" element over the whole set and a "Text" element a second in and two layers up, grouped.
    private static ObjectTemplateItem CreateSetTemplate()
    {
        var back = new Element
        {
            Length = { Value = MediaTime.FromSeconds(4) },
            Name = { Value = "Back" },
        };
        back.AddObject(new RectShape());
        var text = new Element
        {
            Start = { Value = MediaTime.FromSeconds(1) },
            Length = { Value = MediaTime.FromSeconds(2) },
            ZIndex = { Value = 2 },
            Name = { Value = "Text" },
        };
        text.AddObject(new EllipseShape());
        var set = new ElementSet();
        set.Elements.AddRange([back, text]);
        set.Groups.Add([back.Id.Value, text.Id.Value]);
        return ObjectTemplateItem.CreateFromInstance(set, "Lower third");
    }

    // Where each element starts and lies relative to the earliest and lowest of them.
    private static (MediaTime Start, int Layer)[] Offsets(IReadOnlyList<Element> elements)
    {
        MediaTime start = elements.Min(e => e.Start.Value);
        int layer = elements.Min(e => e.ZIndex.Value);
        return [.. elements.Select(e => (e.Start.Value - start, e.ZIndex.Value - layer))];
    }

    [AvaloniaTest]
    public async Task RegisteredAsyncHandler_MaterializesGroupedElementsAndUnregistersCleanly()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-plugin-handler");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new GroupedTestSourceHandler();
        int initialUndoCount = editor.HistoryManager.UndoCount;
        var description = new ElementDescription(
            MediaTime.FromSeconds(2),
            MediaTime.FromSeconds(3),
            4,
            new TestElementSource("plugin"));

        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);
        ElementAddResult result = await adder.AddAsync([description], CancellationToken.None);
        await registration.DisposeAsync();
        ElementAddResult afterUnregister = await adder.AddAsync([description], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Elements, Has.Count.EqualTo(2));
            Assert.That(result.Elements.Select(element => element.ZIndex.Value), Is.EqualTo(new[] { 4, 5 }));
            Assert.That(editor.Scene.Groups, Has.Count.EqualTo(1));
            Assert.That(editor.Scene.Groups.Single(), Is.EquivalentTo(result.Elements.Select(element => element.Id.Value)));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(initialUndoCount + 1));
            Assert.That(handler.PreflightCount, Is.EqualTo(1));
            Assert.That(handler.MaterializationCount, Is.EqualTo(1));
            Assert.That(handler.LastPreflight!.IsDisposed, Is.True);
            Assert.That(handler.Resource.IsDisposed, Is.True);
            Assert.That(afterUnregister.Failure, Is.TypeOf<UnsupportedElementSourceFailure>());
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_CanChooseLengthWhenDescriptionLeavesItUnspecified()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-handler-length");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new LengthPreservingTestSourceHandler();
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);

        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.FromSeconds(2),
                null,
                4,
                new TestElementSource("handler-defined-length")),
        ], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Elements, Has.Count.EqualTo(1));
            Assert.That(result.Elements[0].Length.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
        }
    }

    [AvaloniaTest]
    public async Task BuiltInHandlers_RejectUnspecifiedLengthDuringPreflight()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-built-in-length");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;

        ElementAddResult engineObject = await adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.Zero,
                null,
                0,
                new ElementSource.EngineObject(() => new RectShape())),
        ], CancellationToken.None);
        ElementAddResult file = await adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.Zero,
                null,
                0,
                new ElementSource.File("/tmp/unused.beutl-test")),
        ], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engineObject.Failure, Is.TypeOf<ElementSourcePreflightFailure>());
            Assert.That(engineObject.Elements, Is.Empty);
            Assert.That(file.Failure, Is.TypeOf<ElementSourcePreflightFailure>());
            Assert.That(file.Elements, Is.Empty);
            Assert.That(editor.Scene.Children, Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_CanReturnExtensibleTypedPreflightFailure()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-plugin-failure");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var failure = new TestElementAddFailure("The plugin rejected this source.");
        var handler = new RejectingTestSourceHandler(failure);
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);

        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.Zero,
                MediaTime.FromSeconds(1),
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
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-plugin-cancellation");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new BlockingTestSourceHandler();
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);
        using var cancellationTokenSource = new CancellationTokenSource();
        ValueTask<ElementAddResult> operation = adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.Zero,
                MediaTime.FromSeconds(1),
                0,
                new TestElementSource("cancel")),
        ], cancellationTokenSource.Token);
        await handler.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellationTokenSource.Cancel();

        OperationCanceledException? canceled =
            await Assert.CatchAsync<OperationCanceledException>(async () => await operation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(canceled, Is.Not.Null);
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
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-handler-lease");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new LeaseBlockingTestSourceHandler();
        IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);
        Task<ElementAddResult> operation = adder.AddAsync(
            [CreateTestDescription("lease", 0)],
            CancellationToken.None).AsTask();
        Task? disposeTask = null;

        try
        {
            await handler.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            disposeTask = registration.DisposeAsync().AsTask();
            // Removing the package retires the handler at once, so a new add finds no handler.
            ElementAddResult retiredProbe = await adder.AddAsync(
                [CreateTestDescription("retired", 1)],
                CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(retiredProbe.Failure, Is.TypeOf<UnsupportedElementSourceFailure>());
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
    }

    [AvaloniaTest]
    public async Task AsyncMaterializationCommitsUnrelatedPendingHistorySeparately()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-history-isolation");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new LeaseBlockingTestSourceHandler();
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);
        Task<ElementAddResult> operation = adder.AddAsync(
            [CreateTestDescription("isolated-history", 0)],
            CancellationToken.None).AsTask();
        int unrelatedValue = 0;

        try
        {
            await handler.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.ReleasePreflight.TrySetResult();
            await handler.MaterializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            unrelatedValue = 1;
            editor.HistoryManager.Record(
                () => unrelatedValue = 1,
                () => unrelatedValue = 0);
            handler.ReleaseMaterialization.TrySetResult();
            ElementAddResult result = await operation.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(2));
                Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
                Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
                Assert.That(unrelatedValue, Is.EqualTo(1));
            }

            Assert.That(editor.HistoryManager.Undo(), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(editor.Scene.Children, Is.Empty);
                Assert.That(unrelatedValue, Is.EqualTo(1));
            }
            Assert.That(editor.HistoryManager.Undo(), Is.True);
            Assert.That(unrelatedValue, Is.Zero);
        }
        finally
        {
            handler.ReleasePreflight.TrySetResult();
            handler.ReleaseMaterialization.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTest]
    public async Task AddAsync_CancelledWhileWaitingForHistoryGateDoesNotCommit()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-history-cancellation");
        var adder = (ElementAdderImpl)editor.GetService(typeof(IElementAdder))!;
        using var historyGateEntered = new ManualResetEventSlim();
        using var releaseHistoryGate = new ManualResetEventSlim();
        using var beforeHistoryTransaction = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        Task blocker = Task.Run(() => editor.HistoryManager.ExecuteInTransaction(() =>
        {
            historyGateEntered.Set();
            if (!releaseHistoryGate.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The blocking history transaction was not released.");
        }, "Block history"));
        Assert.That(historyGateEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        adder.BeforeHistoryTransaction = beforeHistoryTransaction.Set;
        Task cancelAndRelease = Task.Run(() =>
        {
            if (!beforeHistoryTransaction.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("ElementAdder did not reach the history boundary.");
            cancellation.Cancel();
            releaseHistoryGate.Set();
        });

        OperationCanceledException? thrown;
        try
        {
            thrown = await Assert.CatchAsync<OperationCanceledException>(async () => await adder.AddAsync(
            [
                new ElementDescription(
                    MediaTime.Zero,
                    MediaTime.FromSeconds(1),
                    0,
                    new ElementSource.EngineObject(() => new RectShape())),
            ], cancellation.Token));

            await Task.WhenAll(blocker, cancelAndRelease).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            adder.BeforeHistoryTransaction = null;
            releaseHistoryGate.Set();
            await Task.WhenAll(blocker, cancelAndRelease).WaitAsync(TimeSpan.FromSeconds(5));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown, Is.Not.Null);
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
        }
    }

    [AvaloniaTest]
    public async Task AddAsync_RevalidatesAfterWaitingForHistoryGate()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-gated-revalidation");
        var adder = (ElementAdderImpl)editor.GetService(typeof(IElementAdder))!;
        using var historyGateEntered = new ManualResetEventSlim();
        using var beforeHistoryTransaction = new ManualResetEventSlim();
        var lockedLayer = new TimelineLayer { ZIndex = { Value = 0 }, IsLocked = { Value = true } };
        Task blocker;
        // The blocker holds the history gate from a second thread, so its write is off the UI thread on purpose.
        using (OffUiThreadWriteCheckAttribute.Allow())
        {
            blocker = Task.Run(() => editor.HistoryManager.ExecuteInTransaction(() =>
            {
                historyGateEntered.Set();
                if (!beforeHistoryTransaction.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("ElementAdder did not reach the history boundary.");
                editor.Scene.Layers.Add(lockedLayer);
            }, "Lock target layer"));
        }

        Assert.That(historyGateEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        adder.BeforeHistoryTransaction = beforeHistoryTransaction.Set;

        ElementAddResult result;
        try
        {
            result = await adder.AddAsync(
            [
                new ElementDescription(
                    MediaTime.Zero,
                    MediaTime.FromSeconds(1),
                    0,
                    new ElementSource.EngineObject(() => new RectShape())),
            ], CancellationToken.None);
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            adder.BeforeHistoryTransaction = null;
            beforeHistoryTransaction.Set();
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));
        }

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Failure, Is.TypeOf<LockedElementLayerFailure>());
                Assert.That(editor.Scene.Children, Is.Empty);
                Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(1));
            }
        }
        finally
        {
            using (editor.HistoryManager.SuppressRecording())
            {
                editor.Scene.Layers.Remove(lockedLayer);
            }
        }
    }

    [AvaloniaTest]
    public async Task EditorDispose_OnUiThreadCancelsAwaitingHandlerAndDrainsItsLease()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-dispose-during-await");
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new BlockingTestSourceHandler();
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);
        Task<ElementAddResult> operation = adder.AddAsync(
            [CreateTestDescription("dispose", 0)],
            CancellationToken.None).AsTask();
        await handler.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task closeTask = TestShell.Editor.CloseTabItem(tab).AsTask();

        Assert.That(closeTask.IsCompleted, Is.False);
        OperationCanceledException? cancellation = await Assert.CatchAsync<OperationCanceledException>(
            async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));
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
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-plugin-locked-lease");
        using (editor.HistoryManager.SuppressRecording())
        {
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 5 }, IsLocked = { Value = true } });
        }
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new GroupedTestSourceHandler();
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);

        ElementAddResult result = await adder.AddAsync(
        [
            new ElementDescription(
                MediaTime.Zero,
                MediaTime.FromSeconds(1),
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
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-transfer-ownership");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var successfulHandler = new OwnershipTestSourceHandler();
        await using (IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, successfulHandler))
        {
            ElementAddResult success = await adder.AddAsync(
            [
                new ElementDescription(
                    MediaTime.Zero,
                    MediaTime.FromSeconds(1),
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
        await using IAsyncDisposable rollbackRegistration = PackageSourceHandlers.Register(editor.ExtensionProvider, rollbackHandler);
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
                    MediaTime.FromSeconds(2),
                    MediaTime.FromSeconds(1),
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
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-extension-composition");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        const int packageId = 98_765_432;
        var extension = new TestSourceHandlerExtension(new GroupedTestSourceHandler());

        editor.ExtensionProvider.AddExtensions(packageId, [extension]);
        try
        {
            ElementAddResult result = await adder.AddAsync(
            [
                new ElementDescription(
                    MediaTime.Zero,
                    MediaTime.FromSeconds(1),
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
                MediaTime.FromSeconds(2),
                MediaTime.FromSeconds(1),
                0,
                new TestElementSource("extension-removed")),
        ], CancellationToken.None);

        Assert.That(afterRemoval.Failure, Is.TypeOf<UnsupportedElementSourceFailure>());
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_RejectsDuplicateElementIdsAcrossTheBatchAndScene()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-duplicate-ids");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var fixedId = Guid.NewGuid();
        var handler = new MutationTestSourceHandler((_, element) => element.Id.Value = fixedId);
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);

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
            Assert.That(editor.Scene.Children[0].Id.Value, Is.EqualTo(fixedId));
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_RevalidatesLayerLockAfterAsyncMaterialization()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-late-layer-lock");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new MutationTestSourceHandler((context, _) =>
        {
            using (editor.HistoryManager.SuppressRecording())
            {
                context.Scene.Layers.Add(new TimelineLayer
                {
                    ZIndex = { Value = context.Description.Layer },
                    IsLocked = { Value = true },
                });
            }
        });
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);

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
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-late-scene-change");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        Guid originalId = editor.Scene.Id.Value;
        Guid changedId = Guid.NewGuid();
        var handler = new MutationTestSourceHandler((context, _) =>
        {
            using (editor.HistoryManager.SuppressRecording())
            {
                context.Scene.Id.Value = changedId;
            }
        });
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);

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
                editor.Scene.Id.Value = originalId;
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure, Is.TypeOf<ElementSceneChangedFailure>());
            Assert.That(editor.Scene.Children, Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task RegisteredHandler_RevalidatesIdentifierUniquenessAfterAsyncMaterialization()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-late-id-conflict");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        Element? competing = null;
        var handler = new MutationTestSourceHandler((context, materialized) =>
        {
            competing = new Element
            {
                Start = { Value = MediaTime.FromSeconds(10) },
                Length = { Value = MediaTime.FromSeconds(1) },
                ZIndex = { Value = 10 },
                Uri = new Uri(Path.Combine(
                    Path.GetDirectoryName(context.Scene.Uri!.LocalPath)!,
                    $"competing-{Guid.NewGuid():N}.belm")),
            };
            using (editor.HistoryManager.SuppressRecording())
            {
                context.Scene.AddChild(competing);
            }
            materialized.Id.Value = competing.Id.Value;
        });
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);

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
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-release-failure");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var handler = new ThrowingDisposeSourceHandler();
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler);

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
            MediaTime.Zero,
            MediaTime.FromSeconds(1),
            layer,
            new TestElementSource(value));

    [AvaloniaTest]
    public async Task AddAsync_WhenSceneMutationFails_RollsBackBatchAndWritesNoFile()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-mutation-failure");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        string sceneDirectory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
        string[] filesBefore = Directory.GetFiles(sceneDirectory, "*.belm", SearchOption.AllDirectories);
        bool shouldThrow = true;
        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
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
                    MediaTime.Zero,
                    MediaTime.FromSeconds(2),
                    Layer: 0,
                    Source: new ElementSource.EngineObject(() => new RectShape())),
                new ElementDescription(
                    MediaTime.FromSeconds(3),
                    MediaTime.FromSeconds(2),
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
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.Scene.Groups, Is.Empty);
            Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
            Assert.That(
                Directory.GetFiles(sceneDirectory, "*.belm", SearchOption.AllDirectories),
                Is.EqualTo(filesBefore));
        }
    }

    [AvaloniaTest]
    public async Task AddAsync_WhenRollbackLeavesAnElementAttached_KeepsItsUri()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("element-adder-partial-rollback");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        var materialized = new List<Element>();
        var sourceHandler = new MutationTestSourceHandler((_, element) => materialized.Add(element));
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, sourceHandler);
        bool shouldInjectMutationFailure = true;
        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add)
                return;

            if (editor.Scene.Children.Count == 1 && shouldInjectMutationFailure)
            {
                editor.HistoryManager.Record(
                    static () => { },
                    () =>
                    {
                        editor.Scene.AddChild(materialized[1]);
                        throw new InvalidOperationException("Injected rollback failure");
                    });
            }
            else if (editor.Scene.Children.Count == 2 && shouldInjectMutationFailure)
            {
                shouldInjectMutationFailure = false;
                throw new InvalidOperationException("Injected mutation failure");
            }
        };
        editor.Scene.Children.CollectionChanged += handler;

        ElementAddResult result;
        try
        {
            result = await adder.AddAsync(
            [
                CreateTestDescription("first", 0),
                CreateTestDescription("second", 1),
            ], CancellationToken.None);
        }
        finally
        {
            editor.Scene.Children.CollectionChanged -= handler;
        }

        Element retained = materialized[1];
        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Failure, Is.TypeOf<ElementSceneMutationFailure>());
                Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
                Assert.That(editor.Scene.Children[0], Is.SameAs(retained));
                Assert.That(retained.Uri, Is.Not.Null);
                Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
                Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
            }
        }
        finally
        {
            using (editor.HistoryManager.SuppressRecording())
            {
                editor.Scene.Children.Remove(retained);
            }
            File.Delete(retained.Uri!.LocalPath);
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

        public TrackingDisposable Resource { get; } = new();

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
                Start = { Value = context.Description.Start },
                Length = { Value = context.Description.Length!.Value },
                ZIndex = { Value = state.PrimaryLayer },
            };
            var companion = new Element
            {
                Start = { Value = context.Description.Start },
                Length = { Value = context.Description.Length.Value },
                ZIndex = { Value = state.PrimaryLayer + 1 },
            };
            IReadOnlySet<Guid> group = new HashSet<Guid> { primary.Id.Value, companion.Id.Value };
            return ElementSourceMaterializationResult.Materialized(
                new ElementMaterialization(
                    primary,
                    [companion],
                    [group],
                    [ElementMaterializationResource.Temporary(Resource)]));
        }
    }

    // Ready on the description's layer and materializes one element there; the fakes below change a step.
    private class SingleElementSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(TestElementSource);

        public virtual ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(
                CreatePreflight(context),
                [context.Description.Layer]));
        }

        public virtual ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(
                Materialize(context, CreateElement(context))));
        }

        protected virtual IElementSourcePreflight CreatePreflight(ElementSourcePreflightContext context)
            => new TestPreflight(context.Description.Layer);

        protected virtual Element CreateElement(ElementSourceMaterializationContext context) => new()
        {
            Start = { Value = context.Description.Start },
            Length = { Value = context.Description.Length!.Value },
            ZIndex = { Value = context.Description.Layer },
        };

        protected virtual ElementMaterialization Materialize(
            ElementSourceMaterializationContext context,
            Element element)
            => new(element);
    }

    private sealed class LengthPreservingTestSourceHandler : SingleElementSourceHandler
    {
        protected override Element CreateElement(ElementSourceMaterializationContext context) => new()
        {
            Start = { Value = context.Description.Start },
            Length = { Value = MediaTime.FromSeconds(7) },
            ZIndex = { Value = context.Description.Layer },
        };
    }

    private sealed class LeaseBlockingTestSourceHandler : SingleElementSourceHandler
    {
        public TaskCompletionSource PreflightStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleasePreflight { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource MaterializationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseMaterialization { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            PreflightStarted.TrySetResult();
            await ReleasePreflight.Task.WaitAsync(cancellationToken);
            return await base.PreflightAsync(context, cancellationToken);
        }

        public override async ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            MaterializationStarted.TrySetResult();
            await ReleaseMaterialization.Task.WaitAsync(cancellationToken);
            return await base.MaterializeAsync(context, preflight, cancellationToken);
        }
    }

    private sealed class OwnershipTestSourceHandler : SingleElementSourceHandler
    {
        public TestPreflight? Preflight { get; private set; }

        public TrackingDisposable Resource { get; } = new();

        protected override IElementSourcePreflight CreatePreflight(ElementSourcePreflightContext context)
            => Preflight = new TestPreflight(context.Description.Layer);

        protected override ElementMaterialization Materialize(
            ElementSourceMaterializationContext context,
            Element element)
            => new(element, resources: [ElementMaterializationResource.Temporary(Resource)]);
    }

    private sealed class MutationTestSourceHandler(
        Action<ElementSourceMaterializationContext, Element> mutate) : SingleElementSourceHandler
    {
        public override async ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            return await base.MaterializeAsync(context, preflight, cancellationToken);
        }

        protected override ElementMaterialization Materialize(
            ElementSourceMaterializationContext context,
            Element element)
        {
            mutate(context, element);
            return new(element);
        }
    }

    private sealed class ThrowingDisposeSourceHandler : SingleElementSourceHandler
    {
        public ThrowingPreflight Preflight { get; } = new();

        public ThrowingDisposable Resource { get; } = new();

        protected override IElementSourcePreflight CreatePreflight(ElementSourcePreflightContext context)
            => Preflight;

        protected override ElementMaterialization Materialize(
            ElementSourceMaterializationContext context,
            Element element)
            => new(element, resources: [ElementMaterializationResource.Temporary(Resource)]);
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

    private sealed class TestSourceHandlerExtension(IElementSourceHandler handler)
        : ElementSourceHandlerExtension
    {
        public override IReadOnlyCollection<ElementSourceHandlerRegistration> Registrations { get; } =
        [
            new ElementSourceHandlerRegistration(handler),
        ];
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

    private sealed class ThrowingDisposable : IDisposable
    {
        public int DisposeAttempts { get; private set; }

        public void Dispose()
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
}
