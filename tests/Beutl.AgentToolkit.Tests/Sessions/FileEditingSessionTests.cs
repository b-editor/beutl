using Beutl.AgentToolkit.Sessions;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class FileEditingSessionTests
{
    [Test]
    public void SetProjectPath_disambiguates_sidecars_for_scenes_sharing_a_name()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2), Name: "demo"));

        // A second scene sharing the seed scene's name ("demo") is what Save As must not collapse onto
        // the same <name>/<name>.scene sidecar.
        ProjectOperations.AddScene(session.Project, new SceneCreateOptions(
            640, 360, TimeSpan.Zero, TimeSpan.FromSeconds(2), Name: "demo"));

        session.SetProjectPath(Path.Combine(root, "copy.bep"));

        string[] sceneDirs = session.Project.Items.OfType<Scene>()
            .Select(s => Path.GetDirectoryName(s.Uri!.LocalPath)!)
            .ToArray();

        Assert.That(sceneDirs, Is.Unique);
    }

    [Test]
    public void SetProjectPath_does_not_overwrite_existing_sidecar_file()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string existingDir = Path.Combine(root, "copy", "demo");
        Directory.CreateDirectory(existingDir);
        string existingSidecar = Path.Combine(existingDir, "demo.scene");
        File.WriteAllText(existingSidecar, "existing sidecar");
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2), Name: "demo"));

        session.SetProjectPath(Path.Combine(root, "copy.bep"));
        session.Save(skipConflictCheck: true);

        Scene scene = session.Project.Items.OfType<Scene>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(existingSidecar), Is.EqualTo("existing sidecar"));
            Assert.That(scene.Uri!.LocalPath, Is.Not.EqualTo(existingSidecar));
            Assert.That(File.Exists(scene.Uri.LocalPath), Is.True);
            Assert.That(Path.GetFileName(Path.GetDirectoryName(scene.Uri.LocalPath)!), Is.EqualTo("demo-2"));
        });
    }

    [Test]
    public void Save_and_save_as_on_a_disposed_session_throw_session_unavailable()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2)));

        session.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<SessionUnavailableException>(() => session.Save(skipConflictCheck: true));
            Assert.Throws<SessionUnavailableException>(
                () => session.SaveAs(Path.Combine(root, "copy.bep"), skipConflictCheck: true));
        });
    }

    [Test]
    public void Open_project_returns_the_session_created_for_the_requested_path()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string pathA = Path.Combine(root, "a.bep");
        string pathB = Path.Combine(root, "b.bep");
        using var source = new FileSessionSource();
        source.CreateProject(new ProjectCreateOptions(pathA, 640, 360, 30, TimeSpan.FromSeconds(2))).Save(skipConflictCheck: true);
        source.CreateProject(new ProjectCreateOptions(pathB, 640, 360, 30, TimeSpan.FromSeconds(2))).Save(skipConflictCheck: true);

        for (int i = 0; i < 20; i++)
        {
            using var barrier = new Barrier(2);
            FileEditingSession? fromA = null;
            FileEditingSession? fromB = null;
            Task openA = Task.Run(() =>
            {
                barrier.SignalAndWait();
                fromA = source.OpenProject(pathA);
            });
            Task openB = Task.Run(() =>
            {
                barrier.SignalAndWait();
                fromB = source.OpenProject(pathB);
            });
            Task.WaitAll(openA, openB);

            Assert.Multiple(() =>
            {
                Assert.That(fromA!.Project.Uri!.LocalPath, Is.EqualTo(Path.GetFullPath(pathA)));
                Assert.That(fromB!.Project.Uri!.LocalPath, Is.EqualTo(Path.GetFullPath(pathB)));
            });
        }
    }

    [Test]
    public void Invoke_serializes_concurrent_dispatches()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2), Name: "demo"));

        const int threadCount = 8;
        int concurrent = 0;
        int maxObserved = 0;
        using var start = new Barrier(threadCount);

        void Body()
        {
            start.SignalAndWait();
            session.Invoke(() =>
            {
                int now = Interlocked.Increment(ref concurrent);
                InterlockedMax(ref maxObserved, now);
                Thread.Sleep(5);
                Interlocked.Decrement(ref concurrent);
            });
        }

        Thread[] threads = [.. Enumerable.Range(0, threadCount).Select(_ => new Thread(Body))];
        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        // The reconciler runs its read/merge/write critical section inside a single Invoke, so the
        // file dispatcher must admit only one action at a time — parity with the live UI thread.
        Assert.That(maxObserved, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_waits_for_an_in_flight_Invoke()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // Not disposed via `using`: the test disposes the session itself and RecordingPipeline.Dispose is not idempotent.
        var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2), Name: "demo"));

        using var inInvoke = new ManualResetEventSlim();
        using var releaseInvoke = new ManualResetEventSlim();
        bool invokeCompleted = false;
        bool disposeSawCompletedInvoke = false;

        var invokeThread = new Thread(() => session.Invoke(() =>
        {
            inInvoke.Set();
            releaseInvoke.Wait();
            Thread.Sleep(20);
            Volatile.Write(ref invokeCompleted, true);
        }));
        invokeThread.Start();
        inInvoke.Wait();

        var disposeThread = new Thread(() =>
        {
            session.Dispose();
            disposeSawCompletedInvoke = Volatile.Read(ref invokeCompleted);
        });
        disposeThread.Start();

        releaseInvoke.Set();
        invokeThread.Join();
        disposeThread.Join();

        // Dispose blocks on the dispatch lock, so it can only return after the in-flight Invoke released it.
        Assert.That(disposeSawCompletedInvoke, Is.True);
    }

    [Test]
    public void Save_waits_for_an_in_flight_Invoke()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2), Name: "demo"));

        using var inInvoke = new ManualResetEventSlim();
        using var releaseInvoke = new ManualResetEventSlim();
        bool invokeCompleted = false;
        bool saveSawCompletedInvoke = false;

        var invokeThread = new Thread(() => session.Invoke(() =>
        {
            inInvoke.Set();
            releaseInvoke.Wait();
            Thread.Sleep(20);
            Volatile.Write(ref invokeCompleted, true);
        }));
        invokeThread.Start();
        inInvoke.Wait();

        var saveThread = new Thread(() =>
        {
            session.Save(skipConflictCheck: true);
            saveSawCompletedInvoke = Volatile.Read(ref invokeCompleted);
        });
        saveThread.Start();

        releaseInvoke.Set();
        invokeThread.Join();
        saveThread.Join();

        // Save blocks on the dispatch lock, so it can only persist after the in-flight Invoke released it.
        Assert.That(saveSawCompletedInvoke, Is.True);
    }

    [Test]
    public void Invoke_after_dispose_reports_an_unavailable_session_not_an_internal_error()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2), Name: "demo"));

        session.Dispose();

        Assert.Throws<SessionUnavailableException>(() => session.Invoke(() => { }));
    }

    [Test]
    public void SetProjectPath_waits_for_an_in_flight_Invoke()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(2), Name: "demo"));

        using var inInvoke = new ManualResetEventSlim();
        using var releaseInvoke = new ManualResetEventSlim();
        bool invokeCompleted = false;
        bool rehomeSawCompletedInvoke = false;

        var invokeThread = new Thread(() => session.Invoke(() =>
        {
            inInvoke.Set();
            releaseInvoke.Wait();
            Thread.Sleep(20);
            Volatile.Write(ref invokeCompleted, true);
        }));
        invokeThread.Start();
        inInvoke.Wait();

        var rehomeThread = new Thread(() =>
        {
            session.SetProjectPath(Path.Combine(root, "copy.bep"));
            rehomeSawCompletedInvoke = Volatile.Read(ref invokeCompleted);
        });
        rehomeThread.Start();

        releaseInvoke.Set();
        invokeThread.Join();
        rehomeThread.Join();

        // SetProjectPath rehomes under the dispatch lock, so it can only rewrite URIs after the in-flight Invoke released it.
        Assert.That(rehomeSawCompletedInvoke, Is.True);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value > current)
        {
            int prior = Interlocked.CompareExchange(ref target, value, current);
            if (prior == current)
            {
                break;
            }

            current = prior;
        }
    }
}
