using System.Diagnostics;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests;

[NonParallelizable]
public sealed class PerformanceSmokeTests
{
    [Test]
    public void Typical_single_query_and_edit_complete_under_two_seconds()
    {
        var scene = new Scene(1920, 1080, "typical")
        {
            Uri = new Uri(Path.Combine(CreateWorkspace(), "Scene.scene"))
        };
        for (int i = 0; i < 100; i++)
        {
            var element = new Element
            {
                Name = $"element-{i}",
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri.LocalPath)!, $"element-{i}.belm"))
            };
            element.AddObject(new RectShape());
            scene.Children.Add(element);
        }

        using var session = new AgentToolkitTestSession(scene);
        var reconciler = new Reconciler();
        var stopwatch = Stopwatch.StartNew();

        JsonObject desired = session.Documents.Read(scene);
        desired[nameof(Scene.Duration)] = TimeSpan.FromSeconds(12).ToString("c");
        reconciler.Apply(session, desired);

        stopwatch.Stop();
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
