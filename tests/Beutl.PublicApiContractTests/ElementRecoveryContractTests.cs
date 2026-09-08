using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.PublicApiContractTests;

public class ElementRecoveryContractTests
{
    [Test]
    public void PluginRepair_SaveAsUndoRestoresElementAndNestedSidecar()
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
        byte[] originalElement = File.ReadAllBytes(element.Uri.LocalPath);
        byte[] originalTransform = File.ReadAllBytes(transformPath);
        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        Element recovered = recoveredScene.Children.Single();
        using var history = new HistoryManager(recoveredScene, new OperationSequenceGenerator());
        Assert.That(ElementRecoveryService.TryCompleteRepair(recovered, history), Is.False);

        ((RectShape)recovered.Objects.Single()).Transform.CurrentValue = new RotationTransform();
        using (var disposedHistory = new HistoryManager(recoveredScene, new OperationSequenceGenerator()))
        {
            disposedHistory.Dispose();
            Assert.Throws<ObjectDisposedException>(() => ElementRecoveryService.TryCompleteRepair(recovered, disposedHistory));
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);
            Assert.That(File.ReadAllBytes(recovered.Uri!.LocalPath), Is.EqualTo(originalElement));
        }
        Assert.That(ElementRecoveryService.TryCompleteRepair(recovered, history), Is.True);
        history.Commit("Plugin repair");
        var copyUri = new Uri(Path.Combine(root, "copy", "element.belm"));
        CoreSerializer.StoreToUri(recovered, copyUri);
        Assert.That(File.ReadAllBytes(copyUri.LocalPath), Is.Not.EqualTo(originalElement));
        Assert.That(File.Exists(Path.Combine(root, "copy", "nested", "transform.json")), Is.False);

        Assert.That(history.Undo(), Is.True);
        CoreSerializer.StoreToUri(recovered, copyUri);
        Assert.That(File.ReadAllBytes(copyUri.LocalPath), Is.EqualTo(originalElement));
        string copiedTransform = Path.Combine(root, "copy", "nested", "transform.json");
        Assert.That(File.ReadAllBytes(copiedTransform), Is.EqualTo(originalTransform));
        var reopened = (RectShape)CoreSerializer.RestoreFromUri<Element>(copyUri).Objects.Single();
        Assert.That(reopened.Transform.CurrentValue, Is.InstanceOf<FallbackTransform>());
        Assert.That(reopened.Transform.CurrentValue!.Uri, Is.EqualTo(new Uri(copiedTransform)));
    }
}
