using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class AddElementToolTests
{
    [Test]
    public void Add_element_rejects_a_negative_duration()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new ElementTools(manager);

        var result = tools.AddElement(startSeconds: 0, durationSeconds: -1, zIndex: 0, contentKind: "shape", shape: "rect");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("durationSeconds"));
            Assert.That(scene.Children, Is.Empty);
        });
    }

    [Test]
    public void Add_element_rejects_a_negative_start()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new ElementTools(manager);

        var result = tools.AddElement(startSeconds: -0.5, durationSeconds: 1, zIndex: 0, contentKind: "shape", shape: "rect");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("startSeconds"));
        });
    }

    [Test]
    public void Move_element_rejects_a_negative_start_and_a_non_positive_duration()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new ElementTools(manager);
        var added = tools.AddElement(startSeconds: 1, durationSeconds: 2, zIndex: 0, contentKind: "shape", shape: "rect");
        Assert.That(added.IsSuccess, Is.True, added.Error?.Message);
        string elementId = scene.Children[0].Id.ToString();

        var negativeStart = tools.MoveElement(elementId, startSeconds: -0.5);
        var zeroDuration = tools.MoveElement(elementId, durationSeconds: 0);

        Assert.Multiple(() =>
        {
            Assert.That(negativeStart.IsSuccess, Is.False);
            Assert.That(negativeStart.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(negativeStart.Error.Target, Is.EqualTo("startSeconds"));
            Assert.That(zeroDuration.IsSuccess, Is.False);
            Assert.That(zeroDuration.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(zeroDuration.Error.Target, Is.EqualTo("durationSeconds"));
            Assert.That(scene.Children[0].Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(scene.Children[0].Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void Duplicate_element_rejects_a_negative_start()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new ElementTools(manager);
        var added = tools.AddElement(startSeconds: 0, durationSeconds: 1, zIndex: 0, contentKind: "shape", shape: "rect");
        Assert.That(added.IsSuccess, Is.True, added.Error?.Message);
        string elementId = scene.Children[0].Id.ToString();

        var result = tools.DuplicateElement(elementId, startSeconds: -1);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("startSeconds"));
            Assert.That(scene.Children, Has.Count.EqualTo(1));
        });
    }

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
