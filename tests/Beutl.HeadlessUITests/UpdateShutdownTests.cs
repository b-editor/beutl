using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public class UpdateShutdownTests
{
    [AvaloniaTest]
    public async Task A_failed_save_does_not_launch_the_installer_or_dispose_the_editor()
    {
        var (main, scene) = await CreateShellAsync();
        EditorTabItem tab = main.EditorService.SelectedTabItem.Value!;
        bool launched = false;
        try
        {
            scene.Duration = TimeSpan.FromSeconds(73);
            using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
                   {
                       if (step == StorageWriteStep.Replace && path == scene.Uri!.LocalPath)
                           throw new IOException("save failed");
                   }))
            {
                Assert.That(await main.TryDisposeForUpdateAsync(() => launched = true), Is.False);
            }

            Assert.Multiple(() =>
            {
                Assert.That(launched, Is.False);
                Assert.That(main.EditorService.TabItems, Does.Contain(tab));
                Assert.That(((EditViewModel)tab.Context.Value).IsDisposingOrDisposed, Is.False);
                Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).Duration, Is.EqualTo(TimeSpan.FromSeconds(30)));
            });
        }
        finally { await DisposeShellAsync(main); }
    }

    [AvaloniaTest]
    public async Task The_installer_starts_once_after_pending_edits_are_saved()
    {
        var (main, scene) = await CreateShellAsync();
        Uri uri = scene.Uri!;
        scene.Duration = TimeSpan.FromSeconds(73);
        int launches = 0;
        TimeSpan savedAtLaunch = default;
        try
        {
            Assert.That(await main.TryDisposeForUpdateAsync(() =>
            {
                launches++;
                savedAtLaunch = CoreSerializer.RestoreFromUri<Scene>(uri).Duration;
                Task<bool> reentrant = main.TryDisposeForUpdateAsync(() => { launches++; return true; });
                Assert.That(reentrant.IsCompletedSuccessfully && !reentrant.Result, Is.True);
                return true;
            }), Is.True);
            await main.WaitForDisposalAsync();
            Assert.That(await main.TryDisposeForUpdateAsync(() => { launches++; return true; }), Is.False);
            Assert.That(launches, Is.EqualTo(1));
            Assert.That(savedAtLaunch, Is.EqualTo(TimeSpan.FromSeconds(73)));
        }
        finally { await DisposeShellAsync(main); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_failed_installer_launch_leaves_shell_services_usable(bool throws)
    {
        var (main, _) = await CreateShellAsync();
        try
        {
            Exception? failure = null;
            bool accepted = false;
            try
            {
                accepted = await main.TryDisposeForUpdateAsync(() =>
                    throws ? throw new IOException("could not start installer") : false);
            }
            catch (IOException ex) { failure = ex; }

            Assert.That(accepted, Is.False);
            Assert.That(failure is not null, Is.EqualTo(throws));
            Assert.DoesNotThrow(() => main._beutlClients.GetResource<PackageChangesQueue>());
        }
        finally { await DisposeShellAsync(main); }
    }

    [AvaloniaTest]
    public async Task An_update_cannot_start_during_an_in_flight_close()
    {
        var (main, _) = await CreateShellAsync();
        IDisposable writer = await main.EditorService.BeginProjectFileWriteAsync(CancellationToken.None);
        Task<bool> close = main.TryDisposeForWindowCloseAsync();
        bool launched = false;
        try
        {
            Assert.That(close.IsCompleted, Is.False);
            Assert.That(await main.TryDisposeForUpdateAsync(() => launched = true), Is.False);
            Assert.That(launched, Is.False);
        }
        finally
        {
            writer.Dispose();
            await close;
            await DisposeShellAsync(main);
        }
    }

    private static async Task<(MainViewModel Main, Scene Scene)> CreateShellAsync()
    {
        await TestReset.ResetShellAsync();
        var main = new MainViewModel(_ => { });
        main.ExtensionProvider.AddExtensions(0, LoadPrimitiveExtensionTask.PrimitiveExtensions);
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "update-shutdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scene = new Scene
        {
            Uri = new Uri(Path.Combine(directory, "main.scene")),
            Duration = TimeSpan.FromSeconds(30),
        };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        main.EditorService.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (main, scene);
    }

    private static async Task DisposeShellAsync(MainViewModel main)
    {
        main.Dispose();
        await main.WaitForDisposalAsync();
        main.CompleteShutdown();
    }
}
