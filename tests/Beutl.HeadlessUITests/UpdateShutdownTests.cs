using System.Diagnostics;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

public class UpdateShutdownTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_failed_save_does_not_launch_the_installer_or_dispose_the_editor(bool openProject)
    {
        var (main, scene) = await CreateShellAsync();
        if (openProject)
        {
            scene = await OpenProjectAroundAsync(main, scene);
        }
        EditorTabItem tab = main.EditorService.SelectedTabItem.Value!;
        bool launched = false;
        try
        {
            scene.Duration.Value = MediaTime.FromSeconds(73);
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
                Assert.That(main.ProjectService.CurrentProject.Value is not null, Is.EqualTo(openProject));
                Assert.That(main.EditorService.TabItems, Does.Contain(tab));
                Assert.That(((EditViewModel)tab.Context.Value).IsDisposingOrDisposed, Is.False);
                Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).Duration.Value, Is.EqualTo(MediaTime.FromSeconds(30)));
            });
        }
        finally { await DisposeShellAsync(main); }
    }

    [AvaloniaTest]
    public async Task The_installer_starts_once_after_pending_edits_are_saved()
    {
        var (main, scene) = await CreateShellAsync();
        Uri uri = scene.Uri!;
        scene.Duration.Value = MediaTime.FromSeconds(73);
        int launches = 0;
        MediaTime savedAtLaunch = default;
        try
        {
            Assert.That(await main.TryDisposeForUpdateAsync(() =>
            {
                launches++;
                savedAtLaunch = CoreSerializer.RestoreFromUri<Scene>(uri).Duration.Value;
                Task<bool> reentrant = main.TryDisposeForUpdateAsync(() => { launches++; return true; });
                Assert.That(reentrant.IsCompletedSuccessfully && !reentrant.Result, Is.True);
                return true;
            }), Is.True);
            await main.WaitForDisposalAsync();
            Assert.That(await main.TryDisposeForUpdateAsync(() => { launches++; return true; }), Is.False);
            Assert.That(launches, Is.EqualTo(1));
            Assert.That(savedAtLaunch, Is.EqualTo(MediaTime.FromSeconds(73)));
        }
        finally { await DisposeShellAsync(main); }
    }

    [AvaloniaTest]
    public async Task Quitting_starts_a_scheduled_update_once_after_pending_edits_are_saved()
    {
        var (main, scene) = await CreateShellAsync();
        Uri uri = scene.Uri!;
        scene.Duration.Value = MediaTime.FromSeconds(73);
        int launches = 0;
        MediaTime savedAtLaunch = default;
        try
        {
            main.SetUpdateOnQuit(() =>
            {
                launches++;
                savedAtLaunch = CoreSerializer.RestoreFromUri<Scene>(uri).Duration.Value;
            });

            Assert.That(await main.TryDisposeForWindowCloseAsync(), Is.True);
            await main.WaitForDisposalAsync();

            Assert.That(launches, Is.EqualTo(1));
            Assert.That(savedAtLaunch, Is.EqualTo(MediaTime.FromSeconds(73)));
        }
        finally { await DisposeShellAsync(main); }
    }

    [AvaloniaTest]
    public async Task A_failed_save_keeps_the_scheduled_update_for_the_next_attempt_to_quit()
    {
        var (main, scene) = await CreateShellAsync();
        scene.Duration.Value = MediaTime.FromSeconds(73);
        int launches = 0;
        try
        {
            main.SetUpdateOnQuit(() => launches++);
            using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
                   {
                       if (step == StorageWriteStep.Replace && path == scene.Uri!.LocalPath)
                           throw new IOException("save failed");
                   }))
            {
                Assert.That(await main.TryDisposeForWindowCloseAsync(), Is.False);
            }

            Assert.That(launches, Is.Zero);
            Assert.That(await main.TryDisposeForWindowCloseAsync(), Is.True);
            Assert.That(launches, Is.EqualTo(1));
        }
        finally { await DisposeShellAsync(main); }
    }

    [AvaloniaTest]
    public async Task A_scheduled_update_that_cannot_start_does_not_stop_Beutl_quitting()
    {
        var (main, _) = await CreateShellAsync();
        try
        {
            main.SetUpdateOnQuit(() => throw new IOException("could not start installer"));

            Assert.That(await main.TryDisposeForWindowCloseAsync(), Is.True);
        }
        finally { await DisposeShellAsync(main); }
    }

    [AvaloniaTest]
    public async Task A_canceled_update_does_not_start_when_quitting()
    {
        var (main, _) = await CreateShellAsync();
        bool launched = false;
        try
        {
            main.SetUpdateOnQuit(() => launched = true);
            main.SetUpdateOnQuit(null);

            Assert.That(await main.TryDisposeForWindowCloseAsync(), Is.True);
            Assert.That(launched, Is.False);
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

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Failed_updater_handoff_restores_the_open_project_and_selected_scene(bool throws)
    {
        var (main, scene) = await CreateShellAsync();
        Scene activeScene = await OpenProjectAroundAsync(main, scene);
        Project opened = main.ProjectService.CurrentProject.Value!;
        activeScene.Duration.Value = MediaTime.FromSeconds(73);
        try
        {
            if (throws)
            {
                Exception? failure = null;
                try { await main.TryDisposeForUpdateAsync(() => throw new IOException("launch failed")).WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (IOException exception) { failure = exception; }
                Assert.That(failure, Is.TypeOf<IOException>());
            }
            else
                Assert.That(await main.TryDisposeForUpdateAsync(() => false).WaitAsync(TimeSpan.FromSeconds(20)), Is.False);

            Assert.That(main.ProjectService.CurrentProject.Value, Is.SameAs(opened));
            Assert.That(main.EditorService.SelectedTabItem.Value?.Context.Value.Object, Is.SameAs(activeScene));
            Assert.That(((EditViewModel)main.EditorService.SelectedTabItem.Value!.Context.Value).IsDisposingOrDisposed, Is.False);
            Assert.That(activeScene.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(73)));
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(activeScene.Uri!).Duration.Value, Is.EqualTo(MediaTime.FromSeconds(73)));
            Assert.That(await main.TryDisposeForUpdateAsync(() => true).WaitAsync(TimeSpan.FromSeconds(20)), Is.True);
            await main.WaitForDisposalAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.That(main.ProjectService.CurrentProject.Value, Is.Null);
        }
        finally { await DisposeShellAsync(main).WaitAsync(TimeSpan.FromSeconds(20)); }
    }

    [AvaloniaTest]
    public async Task A_restart_saves_pending_edits_and_tells_the_handoff_to_carry_it_on()
    {
        bool? restartAtHandoff = null;
        var (main, scene) = await CreateShellAsync(shell => restartAtHandoff = shell.IsRestartRequested);
        Uri uri = scene.Uri!;
        scene.Duration.Value = MediaTime.FromSeconds(73);
        try
        {
            Assert.That(await main.RestartAsync(), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(main.WaitForDisposalAsync().IsCompleted, Is.True);
                Assert.That(restartAtHandoff, Is.True);
                Assert.That(CoreSerializer.RestoreFromUri<Scene>(uri).Duration.Value, Is.EqualTo(MediaTime.FromSeconds(73)));
            });
        }
        finally { await DisposeShellAsync(main); }
    }

    [AvaloniaTest]
    public async Task A_refused_restart_keeps_Beutl_running_and_a_later_quit_does_not_restart()
    {
        bool? restartAtHandoff = null;
        var (main, scene) = await CreateShellAsync(shell => restartAtHandoff = shell.IsRestartRequested);
        EditorTabItem tab = main.EditorService.SelectedTabItem.Value!;
        scene.Duration.Value = MediaTime.FromSeconds(73);
        try
        {
            using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
                   {
                       if (step == StorageWriteStep.Replace && path == scene.Uri!.LocalPath)
                           throw new IOException("save failed");
                   }))
            {
                Assert.That(await main.RestartAsync(), Is.False);
            }

            Assert.Multiple(() =>
            {
                Assert.That(main.IsRestartRequested, Is.False);
                Assert.That(restartAtHandoff, Is.Null);
                Assert.That(main.EditorService.TabItems, Does.Contain(tab));
            });

            Assert.That(await main.TryDisposeForWindowCloseAsync(), Is.True);
            await main.WaitForDisposalAsync();
            Assert.That(restartAtHandoff, Is.False);
        }
        finally { await DisposeShellAsync(main); }
    }

    [Test]
    public void The_handoff_asks_the_package_tools_to_carry_a_restart_on()
    {
        string directory = Path.Combine(Path.GetTempPath(), "beutl-handoff-" + Guid.NewGuid().ToString("N"));
        var queue = new PackageChangesQueue(Path.Combine(directory, "packageChanges.json"));
        try
        {
            queue.UninstallQueue(new PackageIdentity("Beutl.HandoffTest", NuGetVersion.Parse("1.0.0")));

            ProcessStartInfo restarting = MainViewModel.CreateHandoffStartInfo(queue, restart: true, () => "session")!;
            ProcessStartInfo quitting = MainViewModel.CreateHandoffStartInfo(queue, restart: false, () => "session")!;

            Assert.Multiple(() =>
            {
                Assert.That(StartedProgram(restarting), Is.EqualTo("Beutl.PackageTools.UI"));
                Assert.That(restarting.ArgumentList, Does.Contain("--restart"));
                Assert.That(StartedProgram(quitting), Is.EqualTo("Beutl.PackageTools.UI"));
                Assert.That(quitting.ArgumentList, Does.Not.Contain("--restart"));
                Assert.That(quitting.ArgumentList, Is.SupersetOf(new[] { "--session-id", "session" }));
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Test]
    public void A_restart_with_nothing_to_apply_starts_Beutl_itself()
    {
        var queue = new PackageChangesQueue(
            Path.Combine(Path.GetTempPath(), "beutl-handoff-" + Guid.NewGuid().ToString("N"), "packageChanges.json"));

        Assert.Multiple(() =>
        {
            Assert.That(MainViewModel.CreateHandoffStartInfo(queue, restart: false, () => "session"), Is.Null);
            Assert.That(StartedProgram(MainViewModel.CreateHandoffStartInfo(queue, restart: true, () => "session")!), Is.EqualTo("Beutl"));
        });
    }

    // The program a start info runs: its executable, or the assembly it hands to dotnet.
    private static string StartedProgram(ProcessStartInfo startInfo)
    {
        string path = startInfo.ArgumentList.FirstOrDefault(argument => argument.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            ?? startInfo.FileName;
        string name = Path.GetFileName(path);
        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    // handoff sees the shell when Beutl hands the package changes over on its way out.
    private static async Task<(MainViewModel Main, Scene Scene)> CreateShellAsync(Action<MainViewModel>? handoff = null)
    {
        await TestReset.ResetShellAsync();
        MainViewModel? shell = null;
        var main = new MainViewModel(_ => handoff?.Invoke(shell!));
        shell = main;
        main.ExtensionProvider.AddExtensions(0, LoadPrimitiveExtensionTask.PrimitiveExtensions);
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "update-shutdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scene = new Scene
        {
            Uri = new Uri(Path.Combine(directory, "main.scene")),
            Duration = { Value = MediaTime.FromSeconds(30) },
        };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        main.EditorService.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (main, scene);
    }

    // Saves a project holding the scene beside it, opens it, and returns the opened scene.
    private static async Task<Scene> OpenProjectAroundAsync(MainViewModel main, Scene scene)
    {
        var project = new Project { Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "project.bep")) };
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, project.Uri);
        await main.ProjectService.OpenProject(project.Uri.LocalPath).WaitAsync(TimeSpan.FromSeconds(20));
        return main.ProjectService.CurrentProject.Value!.Items.OfType<Scene>().Single();
    }

    private static async Task DisposeShellAsync(MainViewModel main)
    {
        main.Dispose();
        await main.WaitForDisposalAsync();
        main.CompleteShutdown();
    }
}
