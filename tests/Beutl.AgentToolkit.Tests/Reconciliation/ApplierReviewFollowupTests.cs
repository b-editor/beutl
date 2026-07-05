using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class ApplierReviewFollowupTests
{
    [Test]
    public void Existing_element_storage_uri_is_not_redirected_by_the_desired_document()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        Uri originalUri = element.Uri!;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        JsonObject document = session.Documents.Read(session.Root);
        JsonObject elementJson = FindById(document, element.Id)!;
        elementJson["Uri"] = "/tmp/outside-workspace/redirected.belm";

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: document, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(element.Uri, Is.EqualTo(originalUri));
        });
    }

    [Test]
    public void KeyTime_edit_that_crosses_a_neighbour_re_sorts_the_keyframes()
    {
        Scene scene = CreateSceneWithAnimatedText(out _, out TextBlock text);
        var animation = (KeyFrameAnimation<float>)text.Opacity.Animation!;
        Guid firstId = animation.KeyFrames[0].Id;
        Guid secondId = animation.KeyFrames[1].Id;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = CreateTools(session);

        // Push the first frame (0s) past the second (1s); collection order must follow KeyTime.
        JsonObject document = session.Documents.Read(session.Root);
        FindById(document, firstId)!["KeyTime"] = "00:00:02";

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: document, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(animation.KeyFrames[0].Id, Is.EqualTo(secondId));
            Assert.That(animation.KeyFrames[1].Id, Is.EqualTo(firstId));
            Assert.That(animation.KeyFrames[0].KeyTime, Is.LessThan(animation.KeyFrames[1].KeyTime));
        });
    }

    private static EditTools CreateTools(AgentToolkitTestSession session)
    {
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        return new EditTools(manager);
    }

    private static Scene CreateSceneWithElement(out Element element)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        element.AddObject(new TextBlock { Text = { CurrentValue = "Launch" } });
        scene.Children.Add(element);
        return scene;
    }

    private static Scene CreateSceneWithAnimatedText(out Element element, out TextBlock text)
    {
        Scene scene = CreateSceneWithElement(out element);
        text = element.Objects.OfType<TextBlock>().Single();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0, Easing = new LinearEasing() }, out _);
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 100, Easing = new LinearEasing() }, out _);
        text.Opacity.Animation = animation;
        return scene;
    }

    private static JsonObject? FindById(JsonNode? node, Guid id)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? idNode)
                    && Guid.TryParse(idNode?.GetValue<string>(), out Guid parsed)
                    && parsed == id)
                {
                    return obj;
                }

                foreach (KeyValuePair<string, JsonNode?> pair in obj)
                {
                    if (FindById(pair.Value, id) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    if (FindById(item, id) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }
}
