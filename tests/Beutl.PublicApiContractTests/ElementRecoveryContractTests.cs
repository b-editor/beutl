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
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void PluginRepair_SaveUndoRestoresElementAndNestedSidecar(bool rehome, bool retainUri)
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

        string destinationRoot = rehome ? Path.Combine(root, "copy") : root;
        string copiedTransform = Path.Combine(destinationRoot, "nested", "transform.json");
        ((RectShape)recovered.Objects.Single()).Transform.CurrentValue = new RotationTransform
        {
            Uri = retainUri ? new Uri(copiedTransform) : null,
        };
        using (var disposedHistory = new HistoryManager(recoveredScene, new OperationSequenceGenerator()))
        {
            disposedHistory.Dispose();
            Assert.Throws<ObjectDisposedException>(() => ElementRecoveryService.TryCompleteRepair(recovered, disposedHistory));
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);
            Assert.That(File.ReadAllBytes(recovered.Uri!.LocalPath), Is.EqualTo(originalElement));
        }
        Assert.That(ElementRecoveryService.TryCompleteRepair(recovered, history), Is.True);
        history.Commit("Plugin repair");
        var copyUri = new Uri(Path.Combine(destinationRoot, "element.belm"));
        CoreSerializer.StoreToUri(recovered, copyUri);
        if (!retainUri) Assert.That(File.ReadAllBytes(copyUri.LocalPath), Is.Not.EqualTo(originalElement));
        Assert.That(File.Exists(copiedTransform), Is.EqualTo(retainUri));
        if (retainUri) Assert.That(File.ReadAllBytes(copiedTransform), Is.Not.EqualTo(originalTransform));

        Assert.That(history.Undo(), Is.True);
        string unrelatedRoot = Path.Combine(root, "unrelated");
        string unrelatedSidecar = Path.Combine(unrelatedRoot, "nested", "transform.json");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelatedSidecar)!);
        File.WriteAllText(unrelatedSidecar, "Unrelated existing data");
        // The matching main file must not consume Undo's restoration flag if a nested file collides.
        File.WriteAllBytes(Path.Combine(unrelatedRoot, "element.belm"), originalElement);
        Uri? uriBeforeFailedSave = recovered.Uri;
        Assert.Throws<IOException>(() => CoreSerializer.StoreToUri(
            recovered, new Uri(Path.Combine(unrelatedRoot, "element.belm"))));
        Assert.That(File.ReadAllText(unrelatedSidecar), Is.EqualTo("Unrelated existing data"));
        Assert.That(recovered.Uri, Is.EqualTo(uriBeforeFailedSave));
        CoreSerializer.StoreToUri(recovered, copyUri);
        Assert.That(File.ReadAllBytes(copyUri.LocalPath), Is.EqualTo(originalElement));
        Assert.That(File.ReadAllBytes(copiedTransform), Is.EqualTo(originalTransform));
        var reopened = (RectShape)CoreSerializer.RestoreFromUri<Element>(copyUri).Objects.Single();
        Assert.That(reopened.Transform.CurrentValue, Is.InstanceOf<FallbackTransform>());
        Assert.That(reopened.Transform.CurrentValue!.Uri, Is.EqualTo(new Uri(copiedTransform)));
    }
}
