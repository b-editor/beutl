using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using AvaPoint = Avalonia.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public class ElementAddEntryPointTests
{
    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<(EditViewModel Editor, TimelineTabViewModel Timeline)> OpenEditorForNewScene(
        string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        await TestShell.Editor.ActivateTabItemAsync(scene);
        HeadlessTestHelpers.Settle();

        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        var editor = (EditViewModel)tab.Context.Value!;
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()
                                            ?? throw new InvalidOperationException(
                                                "The default editor layout did not create a timeline tab.");
        return (editor, timeline);
    }

    [AvaloniaTest]
    public async Task TimelineAddElement_Success_AddsAndScrollsWithoutNotification()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-add-entry-success");
        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);
        var description = new ElementDescription(
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(2),
            4,
            new ElementSource.EngineObject(() => new RectShape()));

        await timeline.AddElement.ExecuteAsync(description);
        HeadlessTestHelpers.Settle();

        Element created = editor.Scene.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Start, Is.EqualTo(description.Start));
            Assert.That(created.Length, Is.EqualTo(description.Length));
            Assert.That(created.ZIndex, Is.EqualTo(description.Layer));
            Assert.That(created.Objects.OfType<RectShape>().Count(), Is.EqualTo(1));
            Assert.That(scrolls, Has.Count.EqualTo(1));
            Assert.That(scrolls[0].Range, Is.EqualTo(created.Range));
            Assert.That(scrolls[0].ZIndex, Is.EqualTo(created.ZIndex));
            Assert.That(notifications.Notifications, Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task TimelineAddElement_LockedLayer_ShowsWarningWithoutAddingOrScrolling()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-add-entry-locked");
        using (editor.HistoryManager.SuppressRecording())
        {
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = 2, IsLocked = true });
        }

        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);

        await timeline.AddElement.ExecuteAsync(new ElementDescription(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            2,
            new ElementSource.EngineObject(() => new RectShape())));
        HeadlessTestHelpers.Settle();

        Notification notification = notifications.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
            Assert.That(scrolls, Is.Empty);
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(notification.Title, Is.EqualTo(Strings.Lock));
            Assert.That(notification.Message, Is.EqualTo(Strings.LayerIsLocked));
        }
    }

    [AvaloniaTest]
    public async Task TimelineAddElement_GeneralFailure_ShowsErrorWithoutAddingOrScrolling()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-add-entry-failure");
        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);

        await timeline.AddElement.ExecuteAsync(new ElementDescription(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            0,
            new ElementSource.EngineObject(
                static () => throw new InvalidOperationException("Injected element factory failure."))));
        HeadlessTestHelpers.Settle();

        Notification notification = notifications.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
            Assert.That(scrolls, Is.Empty);
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));
            Assert.That(notification.Title, Is.EqualTo(Strings.AddElement));
            Assert.That(notification.Message, Is.EqualTo(MessageStrings.UnexpectedError));
        }
    }

    [AvaloniaTest]
    public async Task TimelineView_TemplateFileDrop_AddsCompleteTemplateAndScrollsToDropTarget()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-template-drop");
        string templatePath = CreateElementTemplateFile(editor, "dropped-template.json");
        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel timelinePanel = view.FindControl<Panel>("TimelinePanel")!;
            Assert.That(timelinePanel, Is.Not.Null);
            using IStorageFile storageFile = await GetStorageFile(window, templatePath);
            using var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile(storageFile));
            var dropPoint = new AvaPoint(180, timeline.CalculateLayerTop(3) + 5);
            int expectedLayer = timeline.ToLayerNumber(dropPoint.Y);
            var args = new DragEventArgs(
                DragDrop.DropEvent,
                transfer,
                timelinePanel,
                dropPoint,
                KeyModifiers.None);
            Element created = await RaiseDropAndWaitForElement(editor.Scene, timelinePanel, args);
            HeadlessTestHelpers.Settle();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(created.Start, Is.EqualTo(timeline.ClickedFrame));
                Assert.That(created.Length, Is.EqualTo(TimeSpan.FromSeconds(7)));
                Assert.That(created.ZIndex, Is.EqualTo(expectedLayer));
                Assert.That(created.Name, Is.EqualTo("Dropped template"));
                Assert.That(created.Objects.OfType<RectShape>().Count(), Is.EqualTo(1));
                Assert.That(scrolls, Has.Count.EqualTo(1));
                Assert.That(scrolls[0].Range, Is.EqualTo(created.Range));
                Assert.That(scrolls[0].ZIndex, Is.EqualTo(created.ZIndex));
                Assert.That(notifications.Notifications, Is.Empty);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task PlayerView_ImageFileDrop_RoutesThroughTimelineAndScrollsToAddedElement()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("player-file-drop");
        string imagePath = CreatePngFile(editor, "dropped-image.png");
        editor.Player.CurrentFrame.Value = TimeSpan.FromSeconds(4);
        editor.Player.PreviewImage.Value = Ref<Bitmap>.Create(new Bitmap(
            editor.Scene.FrameSize.Width,
            editor.Scene.FrameSize.Height));
        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);
        var view = new PlayerView { DataContext = editor.Player };
        view.image.Width = editor.Scene.FrameSize.Width;
        view.image.Height = editor.Scene.FrameSize.Height;
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel framePanel = view.FindControl<Panel>("framePanel")!;
            Assert.That(framePanel, Is.Not.Null);
            var previewSize = new Avalonia.Size(editor.Scene.FrameSize.Width, editor.Scene.FrameSize.Height);
            view.image.Measure(previewSize);
            view.image.Arrange(new Avalonia.Rect(0, 0, previewSize.Width, previewSize.Height));
            Assert.That(view.image.Bounds.Width, Is.GreaterThan(0));
            Assert.That(view.image.Bounds.Height, Is.GreaterThan(0));
            using IStorageFile storageFile = await GetStorageFile(window, imagePath);
            using var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile(storageFile));
            var imageCenter = new AvaPoint(view.image.Bounds.Width / 2, view.image.Bounds.Height / 2);
            var args = new DragEventArgs(
                DragDrop.DropEvent,
                transfer,
                view.image,
                imageCenter,
                KeyModifiers.None);
            Element created = await RaiseDropAndWaitForElement(editor.Scene, framePanel, args);
            HeadlessTestHelpers.Settle();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(created.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
                Assert.That(created.Length, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(created.ZIndex, Is.Zero);
                Assert.That(created.Objects.OfType<SourceImage>().Count(), Is.EqualTo(1));
                Assert.That(scrolls, Has.Count.EqualTo(1));
                Assert.That(scrolls[0].Range, Is.EqualTo(created.Range));
                Assert.That(scrolls[0].ZIndex, Is.EqualTo(created.ZIndex));
                Assert.That(notifications.Notifications, Is.Empty);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task PlayerView_DropWithoutTimelineSurfacesLockedAndUnsupportedFailures()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("player-drop-without-timeline");
        await editor.CloseToolTabAsync(timeline);
        Assert.That(editor.FindToolTab<TimelineTabViewModel>(), Is.Null);
        using (editor.HistoryManager.SuppressRecording())
        {
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = 0, IsLocked = true });
        }
        string imagePath = CreatePngFile(editor, "locked-image.png");
        string unsupportedPath = Path.Combine(
            Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!,
            "unsupported.drop-test");
        await File.WriteAllTextAsync(unsupportedPath, "unsupported");
        editor.Player.CurrentFrame.Value = TimeSpan.Zero;
        editor.Player.PreviewImage.Value = Ref<Bitmap>.Create(new Bitmap(
            editor.Scene.FrameSize.Width,
            editor.Scene.FrameSize.Height));
        using var notifications = new NotificationCapture();
        var view = new PlayerView { DataContext = editor.Player };
        view.image.Width = editor.Scene.FrameSize.Width;
        view.image.Height = editor.Scene.FrameSize.Height;
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel framePanel = view.FindControl<Panel>("framePanel")!;
            var previewSize = new Avalonia.Size(editor.Scene.FrameSize.Width, editor.Scene.FrameSize.Height);
            view.image.Measure(previewSize);
            view.image.Arrange(new Avalonia.Rect(0, 0, previewSize.Width, previewSize.Height));
            var imageCenter = new AvaPoint(view.image.Bounds.Width / 2, view.image.Bounds.Height / 2);

            using IStorageFile lockedFile = await GetStorageFile(window, imagePath);
            using var lockedTransfer = new DataTransfer();
            lockedTransfer.Add(DataTransferItem.CreateFile(lockedFile));
            var lockedDrop = new DragEventArgs(
                DragDrop.DropEvent,
                lockedTransfer,
                view.image,
                imageCenter,
                KeyModifiers.None);
            framePanel.RaiseEvent(lockedDrop);
            await WaitUntilAsync(() => notifications.Notifications.Count >= 1);

            editor.Scene.Layers.Single(layer => layer.ZIndex == 0).IsLocked = false;
            using IStorageFile unsupportedFile = await GetStorageFile(window, unsupportedPath);
            using var unsupportedTransfer = new DataTransfer();
            unsupportedTransfer.Add(DataTransferItem.CreateFile(unsupportedFile));
            var unsupportedDrop = new DragEventArgs(
                DragDrop.DropEvent,
                unsupportedTransfer,
                view.image,
                imageCenter,
                KeyModifiers.None);
            framePanel.RaiseEvent(unsupportedDrop);
            await WaitUntilAsync(() => notifications.Notifications.Count >= 2);
            HeadlessTestHelpers.Settle();

            Notification[] shown = notifications.Notifications.ToArray();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(lockedDrop.Handled, Is.True);
                Assert.That(unsupportedDrop.Handled, Is.True);
                Assert.That(editor.Scene.Children, Is.Empty);
                Assert.That(shown[0].Type, Is.EqualTo(NotificationType.Warning));
                Assert.That(shown[0].Title, Is.EqualTo(Strings.Lock));
                Assert.That(shown[0].Message, Is.EqualTo(Strings.LayerIsLocked));
                Assert.That(shown[1].Type, Is.EqualTo(NotificationType.Error));
                Assert.That(shown[1].Title, Is.EqualTo(Strings.AddElement));
                Assert.That(shown[1].Message, Is.EqualTo(MessageStrings.UnexpectedError));
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [Test]
    public async Task PlayerDropContainsEditorLifecycleCancellation()
    {
        var description = new ElementDescription(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            0,
            new ElementSource.EngineObject(() => new RectShape()));

        foreach (Exception failure in new Exception[]
                 {
                     new OperationCanceledException(),
                     new ObjectDisposedException("editor"),
                 })
        {
            ElementAddResult? result = await PlayerView.AddPlayerDropAsync(
                new FailingElementAdder(failure),
                description);
            Assert.That(result, Is.Null);
        }
    }

    [AvaloniaTest]
    public async Task PlayerDropThroughTimelineAwaitsAndContainsEditorLifecycleCancellation()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, _) = await OpenEditorForNewScene("player-timeline-drop-cancellation");
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        var handler = new BlockingFileSourceHandler();
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        _ = adder.SourceHandlers.Register(new ElementSourceHandlerRegistration(
            handler,
            ElementSourceHandlerRegistrationMode.Replace));
        var view = new PlayerView();
        var description = new ElementDescription(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            0,
            new ElementSource.File("pending.png"));

        Task drop = view.AddElement(editor, description);
        await handler.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(drop.IsCompleted, Is.False);

        await TestShell.Editor.CloseTabItem(tab);
        await drop.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(handler.MaterializationCalled, Is.False);
            Assert.That(editor.Scene, Is.Null);
        });
    }

    private static string CreateElementTemplateFile(EditViewModel editor, string fileName)
    {
        var source = new Element
        {
            Start = TimeSpan.FromSeconds(20),
            Length = TimeSpan.FromSeconds(7),
            ZIndex = 8,
            Name = "Dropped template",
        };
        source.AddObject(new RectShape());
        ObjectTemplateItem template = ObjectTemplateItem.CreateFromInstance(source, "Drop template");
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, fileName);
        File.WriteAllText(
            path,
            ObjectTemplateItem.ToJson(template).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string CreatePngFile(EditViewModel editor, string fileName)
    {
        const string Png =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, fileName);
        File.WriteAllBytes(path, Convert.FromBase64String(Png));
        return path;
    }

    private static async Task<IStorageFile> GetStorageFile(Window window, string path)
    {
        return await window.StorageProvider.TryGetFileFromPathAsync(path)
               ?? throw new InvalidOperationException($"The headless storage provider did not resolve '{path}'.");
    }

    private static async Task<Element> RaiseDropAndWaitForElement(
        Scene scene,
        Control target,
        DragEventArgs args)
    {
        var completion = new TaskCompletionSource<Element>(TaskCreationOptions.RunContinuationsAsynchronously);
        NotifyCollectionChangedEventHandler handler = (_, eventArgs) =>
        {
            if (eventArgs.Action != NotifyCollectionChangedAction.Add
                || eventArgs.NewItems?.OfType<Element>().FirstOrDefault() is not { } element)
            {
                return;
            }

            completion.TrySetResult(element);
        };
        scene.Children.CollectionChanged += handler;
        try
        {
            target.RaiseEvent(args);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            scene.Children.CollectionChanged -= handler;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        const int DelayMilliseconds = 10;
        for (int attempt = 0; attempt < 500; attempt++)
        {
            if (condition())
                return;
            await Task.Delay(DelayMilliseconds);
        }

        Assert.Fail("The expected asynchronous UI state was not reached.");
    }

    private sealed class NotificationCapture : INotificationServiceHandler, IDisposable
    {
        private readonly INotificationServiceHandler _previousHandler;

        public NotificationCapture()
        {
            _previousHandler = NotificationService.Handler;
            NotificationService.Handler = this;
        }

        public ConcurrentQueue<Notification> Notifications { get; } = new();

        public void Show(Notification notification) => Notifications.Enqueue(notification);

        public Notification Single()
        {
            Assert.That(Notifications, Has.Count.EqualTo(1));
            return Notifications.Single();
        }

        public void Dispose()
        {
            NotificationService.Handler = _previousHandler;
        }
    }

    private sealed class FailingElementAdder(Exception failure) : IElementAdder
    {
        public IElementSourceHandlerRegistry SourceHandlers => null!;

        public ValueTask<ElementAddResult> AddAsync(
            IReadOnlyList<ElementDescription> descriptions,
            CancellationToken cancellationToken)
            => ValueTask.FromException<ElementAddResult>(failure);
    }

    private sealed class BlockingFileSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(ElementSource.File);

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
}
