using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.ProjectSystem;
using MergePatchApplier = Beutl.AgentToolkit.MergePatch.MergePatch;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// The validation copy has no project, so it reads a document's times on the session's frame grid, as the live
// apply does, and checks the values the edit will have.
public sealed class ValidationSandboxFrameGridTests
{
    private static readonly Rational s_rate = new(30);

    [Test]
    public void A_start_typed_at_a_frame_is_that_frame_in_the_validation_copy_and_live()
    {
        Scene scene = CreateSceneInProject();
        Element element = AddElement(scene);
        using var session = new AgentToolkitTestSession(scene);
        var patch = new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = element.Id.Value.ToString(),
                [nameof(Element.Start)] = "00:00:01.0333333",
            }),
        };
        JsonObject current = session.Documents.Read(scene);
        var desired = (JsonObject)MergePatchApplier.Apply(current.DeepClone(), patch)!;

        var sandbox = (Scene)Reconciler.BuildValidationSandbox(session, current, desired);
        ToolResult<ApplyEditResponse> result = session.CreateEditTools()
            .ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        MediaTime frame31 = MediaTime.FromFrames(31, s_rate);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(sandbox.Children.Single().Start.Value, Is.EqualTo(frame31));
            Assert.That(element.Start.Value, Is.EqualTo(frame31));
        });
    }

    // A tick reads as frame 0, as it does live, so the copy has the zero duration the scene would get.
    [Test]
    public void A_scene_duration_of_a_tick_is_rejected()
    {
        Scene scene = CreateSceneInProject();
        MediaTime before = scene.Duration.Value;
        using var session = new AgentToolkitTestSession(scene);

        ToolResult<ApplyEditResponse> result = session.CreateEditTools().ApplyEdit(
            patch: new JsonObject { [nameof(Scene.Duration)] = "00:00:00.0000001" },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain("must be positive"));
            Assert.That(scene.Duration.Value, Is.EqualTo(before));
        });
    }

    private static Scene CreateSceneInProject()
    {
        Scene scene = TestWorkspace.CreateScene();
        _ = new Project { FrameRate = { Value = s_rate }, Items = { scene } };
        return scene;
    }

    private static Element AddElement(Scene scene)
    {
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "element.belm")),
        };
        scene.Children.Add(element);
        return element;
    }
}
