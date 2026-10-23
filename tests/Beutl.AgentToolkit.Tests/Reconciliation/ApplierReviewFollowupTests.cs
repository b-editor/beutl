using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
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
        EditTools tools = session.CreateEditTools();

        JsonObject document = session.Documents.Read(session.Root);
        JsonObject elementJson = FindById(document, element.Id.Value)!;
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
        var animation = (KeyFrameAnimation<float>)text.Opacity.GetKeyFrameAnimation()!;
        Guid firstId = animation.KeyFrames[0].Id.Value;
        Guid secondId = animation.KeyFrames[1].Id.Value;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        // Push the first frame (0s) past the second (1s); collection order must follow KeyTime.
        JsonObject document = session.Documents.Read(session.Root);
        FindById(document, firstId)!["KeyTime"] = "00:00:02";

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: document, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(animation.KeyFrames[0].Id.Value, Is.EqualTo(secondId));
            Assert.That(animation.KeyFrames[1].Id.Value, Is.EqualTo(firstId));
            Assert.That(animation.KeyFrames[0].KeyTime.Value, Is.LessThan(animation.KeyFrames[1].KeyTime.Value));
        });
    }

    [Test]
    public void Unrelated_edit_preserves_lossy_easing_storage_suppression()
    {
        string dir = TestWorkspace.CreateDirectory();
        try
        {
            var sceneUri = new Uri(Path.Combine(dir, "Scene.scene"));
            string elementPath = Path.Combine(dir, "element.belm");
            var scene = new Scene(1920, 1080, "Scene") { Uri = sceneUri };
            var element = new Element
            {
                Name = { Value = "Recovered" },
                Length = { Value = MediaTime.FromSeconds(2) },
                Uri = new Uri(elementPath),
            };
            var text = new TextBlock { Text = { Value = "Title" } };
            var animation = new KeyFrameAnimation<float>();
            var keyFrame = new KeyFrame<float>
            {
                KeyTime = { Value = MediaTime.Zero },
                Value = { Value = 100 },
                Easing = { Value = new LinearEasing() },
            };
            animation.KeyFrames.Add(keyFrame, out _);
            text.Opacity.SetKeyFrameAnimation(animation);
            element.AddObject(text);
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            FindById(elementJson, keyFrame.Id.Value)![nameof(KeyFrame.Easing)]
                = "[Missing.Plugin]Missing.Namespace:MissingEasing";
            File.WriteAllText(elementPath, elementJson.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recovered.Children.Single();
            using var session = new AgentToolkitTestSession(recovered);
            EditTools tools = session.CreateEditTools();
            JsonObject desired = session.Documents.Read(session.Root);
            FindById(desired, recoveredElement.Id.Value)![nameof(CoreObject.Name)] = "Changed";

            ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
                desired: desired,
                schemaVersion: SchemaVersion.Current);
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            // Saving would not write the rename, so it is refused instead of reported as applied (#2968).
            Assert.Multiple(() =>
            {
                Assert.That(apply.IsSuccess, Is.False);
                Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(recoveredElement.Name.Value, Is.EqualTo("Recovered"));
                Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
            });
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void Scene_groups_are_reconciled_after_element_mutations()
    {
        Scene scene = CreateSceneWithElement(out Element first);
        var second = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(4) },
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "second.belm"))
        };
        second.AddObject(new TextBlock { Text = { Value = "Second" } });
        scene.Children.Add(second);
        scene.Groups.Add([first.Id.Value, second.Id.Value]);
        using var session = new AgentToolkitTestSession(scene);

        JsonObject document = session.Documents.Read(session.Root);
        JsonArray elements = (JsonArray)document["Elements"]!;
        JsonObject firstJson = FindById(document, first.Id.Value)!;
        var third = new Element { Start = { Value = MediaTime.FromSeconds(1) }, Length = { Value = MediaTime.FromSeconds(2) } };
        third.AddObject(new TextBlock { Text = { Value = "Third" } });
        JsonObject thirdJson = CoreSerializer.SerializeToJsonObject(third);
        elements.Clear();
        elements.Add(firstJson.DeepClone());
        elements.Add(thirdJson);
        document["Groups"] = new JsonArray($"{first.Id.Value}:{third.Id.Value}");

        session.Documents.Write(session.Root, document);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Children.Select(element => element.Id.Value), Is.EqualTo(new[] { first.Id.Value, third.Id.Value }));
            Assert.That(scene.Groups, Has.Count.EqualTo(1));
            Assert.That(scene.Groups[0], Is.EquivalentTo(new[] { first.Id.Value, third.Id.Value }));
        });
    }

    private static Scene CreateSceneWithElement(out Element element)
    {
        Scene scene = TestWorkspace.CreateScene();
        element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(4));
        element.AddObject(new TextBlock { Text = { Value = "Launch" } });
        return scene;
    }

    private static Scene CreateSceneWithAnimatedText(out Element element, out TextBlock text)
    {
        Scene scene = CreateSceneWithElement(out element);
        text = element.Objects.OfType<TextBlock>().Single();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 0 }, Easing = { Value = new LinearEasing() } }, out _);
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 100 }, Easing = { Value = new LinearEasing() } }, out _);
        text.Opacity.SetKeyFrameAnimation(animation);
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
