using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests;

public sealed class QueryScopingTests
{
    [Test]
    public void Read_document_rootId_returns_bounded_subtree_for_large_scene()
    {
        var scene = new Scene(1920, 1080, "large")
        {
            Uri = new Uri(Path.Combine(CreateWorkspace(), "Scene.scene"))
        };
        Element target = AddElement(scene, "target");
        for (int i = 0; i < 250; i++)
        {
            AddElement(scene, $"other-{i}");
        }

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        var full = tools.ReadDocument();
        var scoped = tools.ReadDocument(target.Id.ToString());

        string scopedJson = scoped.Value!.Document.ToJsonString();
        Assert.Multiple(() =>
        {
            Assert.That(full.Value!.Document.ToJsonString().Length, Is.GreaterThan(scopedJson.Length * 10));
            Assert.That(scopedJson, Does.Contain(target.Id.ToString()));
            Assert.That(scopedJson, Does.Not.Contain("other-249"));
        });
    }

    private static Element AddElement(Scene scene, string name)
    {
        string dir = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        var element = new Element
        {
            Name = name,
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(dir, $"{name}.belm"))
        };
        element.AddObject(new RectShape());
        scene.Children.Add(element);
        return element;
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
