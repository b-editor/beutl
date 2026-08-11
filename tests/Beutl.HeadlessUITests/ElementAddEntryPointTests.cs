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

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();

        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        var editor = (EditViewModel)tab.Context.Value;
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
}
