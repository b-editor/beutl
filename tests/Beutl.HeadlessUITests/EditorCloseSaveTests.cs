using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Beutl.Configuration;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Moq;

namespace Beutl.HeadlessUITests;

public class EditorCloseSaveTests
{
    [AvaloniaTest]
    [TestCase(false, false, "en")]
    [TestCase(false, true, "ja")]
    [TestCase(true, false, "ja")]
    [TestCase(true, true, "en")]
    public async Task Failed_history_replay_allows_new_edits_auto_save_and_close(bool redo, bool closeProject, string culture)
    {
        EditViewModel editor = await OpenEditorAsync();
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        Uri uri = editor.Scene.Uri!;
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        var notifications = new List<Notification>();
        var handler = new Mock<INotificationServiceHandler>();
        handler.Setup(x => x.Show(It.IsAny<Notification>())).Callback<Notification>(notifications.Add);
        try
        {
            NotificationService.Handler = handler.Object;
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            editor.HistoryManager.Record(
                () =>
                {
                    editor.Scene.Duration = TimeSpan.FromSeconds(13);
                    if (redo) throw new IOException("Injected redo failure.");
                },
                () =>
                {
                    editor.Scene.Duration = TimeSpan.FromSeconds(12);
                    if (!redo) throw new IOException("Injected undo failure.");
                });
            editor.HistoryManager.Commit("Failing operation");
            if (redo)
                Assert.That(await editor.UndoAsync(), Is.True);

            Assert.That(redo ? await editor.RedoAsync() : await editor.UndoAsync(), Is.False);
            Assert.That(notifications, Has.Count.EqualTo(1));
            Notification notification = notifications.Single();
            Assert.That(notification.Message, Is.EqualTo(Strings.History_ResetAfterFailure));
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));

            if (Environment.GetEnvironmentVariable("BEUTL_HISTORY_RECOVERY_CAPTURE") is { Length: > 0 } capture)
            {
                var window = new Window
                {
                    Width = 400,
                    Height = 260,
                    Content = new Border
                    {
                        Padding = new Thickness(20),
                        VerticalAlignment = VerticalAlignment.Top,
                        Child = new NotificationServiceHandler().BuildInfoBar(notification, new TaskCompletionSource(), () => { })
                    }
                };
                try
                {
                    window.Show();
                    HeadlessTestHelpers.Settle();
                    window.UpdateLayout();
                    Directory.CreateDirectory(capture);
                    using var frame = new RenderTargetBitmap(new PixelSize(400, 260), new Vector(96, 96));
                    frame.Render(window);
                    frame.Save(Path.Combine(capture, $"history-reset-{culture}-{redo}-{closeProject}.png"), PngBitmapEncoderOptions.Default);
                }
                finally
                {
                    window.Close();
                }
            }

            int stateChanges = 0;
            using var subscription = editor.HistoryManager.StateChanged.Subscribe(_ => stateChanges++);
            editor.Scene.Duration = TimeSpan.FromSeconds(73);
            Assert.DoesNotThrow(() => editor.HistoryManager.Commit("Edit after failed replay"));
            Assert.That(stateChanges, Is.EqualTo(1));

            // Check the actual auto-saved document before the close path can save it again.
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (CoreSerializer.RestoreFromUri<Scene>(uri).Duration != editor.Scene.Duration
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
                HeadlessTestHelpers.Settle();
            }
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(editor.Scene.Duration));

            if (closeProject)
                await TestShell.Project.CloseProjectAsync();
            else
                await TestShell.Editor.CloseTabItem(tab);

            Assert.That(TestShell.Editor.TabItems, Does.Not.Contain(tab));
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
        }
        finally
        {
            NotificationService.Handler = previousHandler;
            CultureInfo.CurrentUICulture = previousCulture;
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Closing_waits_for_a_pending_media_hash_and_persists_it(bool closeProject)
    {
        EditViewModel editor = await OpenEditorAsync();
        await editor.WaitForMediaFingerprintsAsync();
        string media = Path.Combine(BeutlHomeIsolation.CurrentHome!, "close-media.png");
        File.WriteAllBytes(media, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="));
        Uri sceneUri = editor.Scene.Uri!;
        using (editor.HistoryManager.SuppressRecording())
        {
            var source = new Beutl.Media.Source.ImageSource(); source.ReadFrom(new Uri(media));
            var element = new Element { Uri = new Uri(Path.Combine(Path.GetDirectoryName(sceneUri.LocalPath)!, "media.belm")) };
            element.Objects.Add(new Beutl.Graphics.SourceImage { Source = { CurrentValue = source } });
            editor.Scene.Children.Add(element);
        }
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        editor.CaptureMediaFingerprints = async (scene, token) =>
        {
            entered.TrySetResult(); await release.Task.WaitAsync(token);
            await new Beutl.Editor.MissingMediaService().UpdateFingerprintsAsync(scene, token);
        };
        try
        {
            editor.ScheduleMediaFingerprints();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task close = closeProject ? TestShell.Project.CloseProjectAsync()
                : TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!).AsTask();
            Assert.That(close.IsCompleted, Is.False);
            release.SetResult();
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(sceneUri).MediaFingerprints.ContainsKey(new Uri(media).AbsoluteUri), Is.True);
        }
        finally { release.TrySetResult(); await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Closing_flushes_a_pending_nudge(bool closeProject)
    {
        EditViewModel editor = await OpenEditorAsync();
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.FromSeconds(1), Length: TimeSpan.FromSeconds(3), Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        HeadlessTestHelpers.Settle();
        await editor.SaveAsync();
        Element element = editor.Scene.Children.Single();
        Uri uri = element.Uri!;
        var nudge = (IElementNudgeService)editor.GetService(typeof(IElementNudgeService))!;

        nudge.Nudge(editor.Scene, [element], 1);
        TimeSpan expected = element.Start;
        if (closeProject)
            await TestShell.Project.CloseProjectAsync();
        else
            await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!);
        HeadlessTestHelpers.Settle();

        Assert.That(CoreSerializer.RestoreFromUri<Element>(uri).Start, Is.EqualTo(expected));
    }

    [AvaloniaTest]
    public async Task Closing_waits_for_the_file_writer_and_persists_queued_edits()
    {
        EditViewModel editor = await OpenEditorAsync();
        Uri uri = editor.Scene.Uri!;
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        IDisposable writer = await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None);
        Task close;
        try
        {
            editor.Scene.Duration = TimeSpan.FromSeconds(73);
            editor.HistoryManager.Commit();
            HeadlessTestHelpers.Settle();
            close = TestShell.Editor.CloseTabItem(tab).AsTask();
            Assert.That(close.IsCompleted, Is.False);
        }
        finally
        {
            writer.Dispose();
        }

        await close.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Settle();
        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_failed_close_save_keeps_the_editor_available_for_retry(bool closeProject)
    {
        EditViewModel editor = await OpenEditorAsync();
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        Uri uri = editor.Scene.Uri!;
        editor.Scene.Duration = TimeSpan.FromSeconds(73);

        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if (step == StorageWriteStep.Replace && path == uri.LocalPath)
                       throw new IOException("Injected close save failure.");
               }))
        {
            if (closeProject)
            {
                Assert.That(await TestShell.Project.TryCloseProjectAsync(
                    TestShell.Project.CurrentProject.Value!, ProjectService.ProjectCloseIntent.SaveChanges), Is.False);
            }
            else
            {
                await TestShell.Editor.CloseTabItem(tab);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Editor.TabItems, Does.Contain(tab));
            Assert.That(tab.Context.Value, Is.SameAs(editor));
            Assert.That(editor.IsDisposingOrDisposed, Is.False);
            Assert.That(editor.IsEnabled.Value, Is.True);
            Assert.That(editor.Scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
        });
        await TestShell.Editor.CloseTabItem(tab);
        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
    }

    [AvaloniaTest]
    public async Task An_explicit_discard_does_not_persist_pending_edits()
    {
        EditViewModel editor = await OpenEditorAsync();
        Uri uri = editor.Scene.Uri!;
        TimeSpan original = editor.Scene.Duration;
        editor.Scene.Duration = TimeSpan.FromSeconds(73);

        Assert.That(await TestShell.Project.TryCloseProjectAsync(
            TestShell.Project.CurrentProject.Value!, ProjectService.ProjectCloseIntent.DiscardChanges), Is.True);

        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(original));
    }

    [AvaloniaTest]
    public async Task Overlapping_tab_closes_do_not_dispose_the_context_twice()
    {
        EditViewModel editor = await OpenEditorAsync();
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        Uri uri = editor.Scene.Uri!;
        editor.Scene.Duration = TimeSpan.FromSeconds(73);
        IDisposable writer = await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None);
        Task first;
        Task second;
        try
        {
            first = TestShell.Editor.CloseTabItem(tab).AsTask();
            second = TestShell.Editor.CloseTabItem(tab).AsTask();
        }
        finally { writer.Dispose(); }

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(TestShell.Editor.TabItems, Does.Not.Contain(tab));
        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(TimeSpan.FromSeconds(73)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Window_close_is_refused_when_a_standalone_scene_cannot_save(bool asynchronous)
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "standalone-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scene = new Scene { Uri = new Uri(Path.Combine(directory, "main.scene")) };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        scene.Duration = TimeSpan.FromSeconds(73);

        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if (step == StorageWriteStep.Replace && path == scene.Uri.LocalPath)
                       throw new IOException("Injected standalone close failure.");
               }))
        {
            bool closed = asynchronous
                ? await TestShell.MainViewModel.TryDisposeForWindowCloseAsync()
                : TestShell.MainViewModel.TryDisposeForWindowClose();
            Assert.That(closed, Is.False);
        }

        Assert.That(TestShell.Editor.TabItems, Does.Contain(tab));
        Assert.That(((EditViewModel)tab.Context.Value).IsDisposingOrDisposed, Is.False);
        await TestShell.Editor.CloseTabItem(tab);
    }

    [AvaloniaTest]
    public async Task Worktree_transition_teardown_does_not_write_the_old_scene()
    {
        EditViewModel editor = await OpenEditorAsync();
        Uri uri = editor.Scene.Uri!;
        TimeSpan original = editor.Scene.Duration;
        editor.Scene.Duration = TimeSpan.FromSeconds(73);

        using (IDisposable? mutation = TestShell.Editor.TryBeginWorktreeMutation())
        {
            Assert.That(mutation, Is.Not.Null);
            await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!);
        }

        Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration, Is.EqualTo(original));
    }

    private static async Task<EditViewModel> OpenEditorAsync()
    {
        await TestReset.ResetShellAsync();
        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        bool enable = config.EnableForNewProjects;
        try
        {
            config.EnableForNewProjects = false;
            string name = "close-save-" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
            Directory.CreateDirectory(directory);
            Project project = (await TestShell.Project.CreateProject(320, 180, 30, 44100, name, directory))!;
            TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().Single());
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            await editor.SaveAsync();
            return editor;
        }
        finally
        {
            config.EnableForNewProjects = enable;
        }
    }
}
