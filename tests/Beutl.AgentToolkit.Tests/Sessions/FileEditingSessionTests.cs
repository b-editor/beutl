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
