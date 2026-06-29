using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class AddElementToolTests
{
    [Test]
    public void Add_element_mints_new_identity_and_inserts_explicit_rect_shape()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new ElementTools(manager);

        var result = tools.AddElement(0, 1, 0, "shape", shape: "rect");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(scene.Children, Has.Count.EqualTo(1));
            Assert.That(scene.Children[0].Objects, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Add_element_defaults_shape_to_rounded_rect_instead_of_plain_rect()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new ElementTools(manager);

        var result = tools.AddElement(0, 1, 0, "shape");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(scene.Children[0].Objects[0], Is.TypeOf<RoundedRectShape>());
        });
    }
}
