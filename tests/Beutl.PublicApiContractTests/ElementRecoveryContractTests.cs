using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.PublicApiContractTests;

public class ElementRecoveryContractTests
{
    // A plugin object stored in a file of its own (a nested sidecar) whose type is missing loads as a fallback that
    // writes that file back as it was read. Its element stays editable and needs no repair: replacing the fallback
    // is an ordinary edit that saving writes, and undoing it brings the fallback back, which saving writes back to
    // its own file.
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void MissingPluginTypeInANestedSidecar_KeepsTheElementEditable_AndTheSidecarRoundTrips(
        bool rehome,
        bool retainUri)
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scene = new Scene { Uri = new Uri(Path.Combine(root, "scene.scene")) };
        var element = new Element { Uri = new Uri(Path.Combine(root, "element.belm")), Length = TimeSpan.FromSeconds(1) };
        string transformPath = Path.Combine(root, "nested", "transform.json");
        var shape = new RectShape();
        shape.Transform.CurrentValue = new RotationTransform { Uri = new Uri(transformPath) };
        element.AddObject(shape);
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        JsonObject json = JsonNode.Parse(File.ReadAllText(transformPath))!.AsObject();
        json["$type"] = "[Missing.Plugin]Missing:Transform";
        File.WriteAllText(transformPath, json.ToJsonString());
        byte[] originalTransform = File.ReadAllBytes(transformPath);
        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        Element recovered = recoveredScene.Children.Single();
        var recoveredShape = (RectShape)recovered.Objects.Single();
        Transform? fallback = recoveredShape.Transform.CurrentValue;
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(recoveredScene, sequence);
        using var observer = new CoreObjectOperationObserver(null, recoveredScene, sequence);
        using IDisposable subscription = history.Subscribe(observer);
        Assert.Multiple(() =>
        {
            Assert.That(fallback, Is.InstanceOf<FallbackTransform>());
            Assert.That(recoveredScene.IsElementLocked(recovered), Is.False);
            Assert.That(ElementRecoveryService.TryCompleteRepair(recovered, history), Is.False);
        });

        string destinationRoot = rehome ? Path.Combine(root, "copy") : root;
        string copiedTransform = Path.Combine(destinationRoot, "nested", "transform.json");
        recoveredShape.Transform.CurrentValue = new RotationTransform
        {
            Uri = retainUri ? new Uri(copiedTransform) : null,
        };
        history.Commit("Replace the transform");
        var copyUri = new Uri(Path.Combine(destinationRoot, "element.belm"));
        CoreSerializer.StoreToUri(recovered, copyUri);
        JsonNode? savedReference = ReadJson(copyUri.LocalPath)[nameof(Element.Objects)]![0]![nameof(RectShape.Transform)];
        Transform? savedTransform = ((RectShape)CoreSerializer.RestoreFromUri<Element>(copyUri).Objects.Single())
            .Transform.CurrentValue;
        Assert.Multiple(() =>
        {
            Assert.That(savedTransform, Is.TypeOf<RotationTransform>());
            Assert.That(File.Exists(copiedTransform), Is.EqualTo(retainUri));
            if (retainUri)
            {
                Assert.That(savedReference!.GetValue<string>(), Is.EqualTo("nested/transform.json"));
                Assert.That(savedTransform!.Uri, Is.EqualTo(new Uri(copiedTransform)));
                Assert.That(File.ReadAllBytes(copiedTransform), Is.Not.EqualTo(originalTransform));
            }
            else
            {
                Assert.That(savedReference, Is.InstanceOf<JsonObject>(), "The transform is saved in the element file.");
            }
        });

        Assert.That(history.Undo(), Is.True);
        Assert.That(recoveredShape.Transform.CurrentValue, Is.SameAs(fallback));
        CoreSerializer.StoreToUri(recovered, copyUri);
        var reopened = (RectShape)CoreSerializer.RestoreFromUri<Element>(copyUri).Objects.Single();
        Assert.Multiple(() =>
        {
            // The fallback writes the JSON it was read from back to its own file; only the formatting may differ.
            Assert.That(JsonNode.DeepEquals(ReadJson(transformPath), JsonNode.Parse(originalTransform)), Is.True);
            Assert.That(reopened.Transform.CurrentValue, Is.InstanceOf<FallbackTransform>());
            Assert.That(reopened.Transform.CurrentValue!.Uri, Is.EqualTo(new Uri(transformPath)));
        });
    }

    private static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}
