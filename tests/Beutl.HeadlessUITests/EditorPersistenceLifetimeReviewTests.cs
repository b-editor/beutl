using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public class EditorPersistenceLifetimeReviewTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Close_WhenViewStateDestinationIsOccupied_ReleasesEditorAndTabBeforeReportingSaveFailure(
        bool failSelectionCleanupObserver, bool failTabCleanupObserver)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "dispose-occupied-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var scene = new Scene(320, 180, "dispose-occupied")
        {
            Uri = new Uri(Path.Combine(workspace, "main.scene")),
        };
        var extensions = new ExtensionProvider();
        var service = new EditorService(extensions);
        var editor = new EditViewModel(scene, extensions, service);
        var tab = new EditorTabItem(editor);
        service.TabItems.Add(tab);
        service.SelectedTabItem.Value = tab;
        FrameCacheManager cache = editor.FrameCacheManager.Value;
        PlayerViewModel player = editor.Player;
        bool contextCompleted = false;
        bool filePathCompleted = false;
        using IDisposable contextSubscription = tab.Context.Subscribe(_ => { }, () => contextCompleted = true);
        using IDisposable filePathSubscription = tab.FilePath.Subscribe(_ => { }, () => filePathCompleted = true);
        string stateFile = Path.Combine(workspace,
            EditorConstants.BeutlFolder, EditorConstants.ViewStateFolder, "main.config");
        IDisposable? selectionObserver = null;
        IDisposable? tabObserver = null;
        bool selectionObserverInvoked = false;
        bool tabObserverInvoked = false;
        try
        {
            Assert.That(await editor.SaveAsync(), Is.True);
            // A directory at the destination rejects replacement on every host, without relying
            // on Windows-only sharing behavior.
            File.Delete(stateFile);
            Directory.CreateDirectory(stateFile);
            if (failSelectionCleanupObserver)
            {
                var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
                selection.Select(scene);
                selectionObserver = selection.SelectedObject.Skip(1).Subscribe(_ =>
                {
                    selectionObserverInvoked = true;
                    throw new InvalidOperationException("Injected selection cleanup observer failure.");
                });
            }
            if (failTabCleanupObserver)
            {
                tabObserver = tab.Context.Skip(1).Subscribe(context =>
                {
                    if (context is null)
                    {
                        tabObserverInvoked = true;
                        throw new InvalidOperationException("Injected tab cleanup observer failure.");
                    }
                });
            }

            Exception? error = await Assert.CatchAsync<Exception>(async () =>
                await service.CloseTabItem(tab, saveChanges: false).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.That(error, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            Assert.That(error!.StackTrace, Does.Contain("SaveState"), "Preserve the view-state failure ahead of subscriber failures.");
            Assert.Multiple(() =>
            {
                Assert.That(service.TabItems, Does.Not.Contain(tab));
                Assert.That(editor.Scene, Is.Null);
                Assert.That(cache.IsDisposed, Is.True);
                Assert.That(player.Scene, Is.Null);
                Assert.That(editor.Player, Is.Null);
                Assert.That(tab.Context.Value, Is.Null);
                Assert.That(contextCompleted, Is.True, "Context cleanup continues after a notification fails.");
                Assert.That(filePathCompleted, Is.True, "A prior cleanup failure does not strand later tab subscriptions.");
                Assert.That(selectionObserverInvoked, Is.EqualTo(failSelectionCleanupObserver));
                Assert.That(tabObserverInvoked, Is.EqualTo(failTabCleanupObserver));
            });
        }
        finally
        {
            selectionObserver?.Dispose();
            tabObserver?.Dispose();
            if (Directory.Exists(stateFile))
                Directory.Delete(stateFile);
            service.SelectedTabItem.Value = null;
            service.TabItems.Remove(tab);
            if (editor.Scene is not null)
                await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
