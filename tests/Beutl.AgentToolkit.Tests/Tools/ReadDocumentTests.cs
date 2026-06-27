using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class ReadDocumentTests
{
    [Test]
    public void Read_document_returns_full_document_and_scoped_subtree()
    {
        var scene = new Scene(1920, 1080, "Scene");
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        scene.Uri = new Uri(Path.Combine(dir, "Scene.scene"));
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2)
        };
        element.AddObject(new TextBlock { Text = { CurrentValue = "Scoped" } });
        element.Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"));
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        var full = tools.ReadDocument();
        var scoped = tools.ReadDocument(element.Id.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(full.IsSuccess, Is.True);
            Assert.That(full.Value!.Document["Elements"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(scoped.IsSuccess, Is.True);
            Assert.That(scoped.Value!.Document["Id"]!.GetValue<string>(), Is.EqualTo(element.Id.ToString()));
            Assert.That(scoped.Value.SchemaVersion, Is.EqualTo(SchemaVersion.Current));
        });
    }

    [Test]
    public void Read_document_unknown_root_id_returns_stale_handle()
    {
        using var session = new AgentToolkitTestSession(new Scene());
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        var result = tools.ReadDocument(Guid.NewGuid().ToString());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.StaleHandle));
        });
    }
}
