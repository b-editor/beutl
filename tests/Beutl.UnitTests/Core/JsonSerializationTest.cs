using System.Text.Json.Nodes;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Core;

public class JsonSerializationTest
{
    private class TestSerializable : CoreObject
    {
        public TestSerializable? Instance { get; set; }

        public TestSerializable? Reference { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Instance), Instance);
            context.SetValue(nameof(Reference), Reference?.Id);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Instance = context.GetValue<TestSerializable?>(nameof(Instance));
            var id = context.GetValue<Guid?>(nameof(Reference));
            if (id.HasValue)
            {
                context.Resolve(id.Value, o => Reference = o as TestSerializable);
            }
        }
    }

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    [Test]
    public void Serialize()
    {
        BeutlApplication app = BeutlApplication.Current;
        var proj = new Project();
        var basePath = Path.GetFullPath(ArtifactProvider.GetArtifactDirectory());

        CoreSerializer.StoreToUri(proj, UriHelper.CreateFromPath(Path.Combine(basePath, $"0.bproj")));
        app.Project = proj;

        var scene = new Scene();
        CoreSerializer.StoreToUri(scene, UriHelper.CreateFromPath(Path.Combine(basePath, $"0.scene")));
        proj.Items.Add(scene);
        var elm1 = new Element();
        CoreSerializer.StoreToUri(elm1, UriHelper.CreateFromPath(Path.Combine(basePath, $"0.layer")));
        scene.AddChild(elm1);
        elm1.Objects.Add(new EllipseShape());
        elm1.Objects.Add(new DrawableDecorator());

        var elm2 = new Element { ZIndex = 2 };
        CoreSerializer.StoreToUri(elm2, UriHelper.CreateFromPath(Path.Combine(basePath, $"1.layer")));
        scene.AddChild(elm2);
        var nodeGraphDrawable = new NodeGraphDrawable();
        elm2.Objects.Add(nodeGraphDrawable);
        var graphModel = nodeGraphDrawable.Model.CurrentValue!;
        var rectNode = new RectGeometryNode();
        var shapeNode = new GeometryShapeNode();
        var outNode = new OutputNode();
        graphModel.Nodes.Add(rectNode);
        graphModel.Nodes.Add(shapeNode);
        graphModel.Nodes.Add(outNode);

        graphModel.Connect((IInputPort)shapeNode.Items[1], rectNode.OutputPort);
        graphModel.Connect((IInputPort)outNode.Items[0], (IOutputPort)shapeNode.Items[0]);
    }

    // SaveReferencedObjectsのテスト
    [Test]
    public void SerializeWithSaveReferencedObjects()
    {
        var basePath = Path.GetFullPath(ArtifactProvider.GetArtifactDirectory());
        var projPath = Path.Combine(basePath, "0.bproj");
        var scenePath = Path.Combine(basePath, "0", "0.scene");
        var layerPath = Path.Combine(basePath, "0", "1", "0.layer");

        // 既存のファイルを削除
        if (File.Exists(projPath)) File.Delete(projPath);
        if (File.Exists(scenePath)) File.Delete(scenePath);
        if (File.Exists(layerPath)) File.Delete(layerPath);

        BeutlApplication app = BeutlApplication.Current;
        var proj = new Project { Uri = UriHelper.CreateFromPath(projPath) };
        var scene = new Scene { Uri = UriHelper.CreateFromPath(scenePath) };
        var elm1 = new Element { Uri = UriHelper.CreateFromPath(layerPath) };
        scene.AddChild(elm1);
        proj.Items.Add(scene);
        app.Project = proj;

        CoreSerializer.StoreToUri(proj, proj.Uri, CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects);

        // ファイルが存在することを確認
        Assert.That(File.Exists(projPath), Is.True);
        Assert.That(File.Exists(scenePath), Is.True);
        Assert.That(File.Exists(layerPath), Is.True);
    }

    [Test]
    public void DeserializeWithSaveReferencedObjects()
    {
        SerializeWithSaveReferencedObjects();

        var basePath = Path.GetFullPath(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "../SerializeWithSaveReferencedObjects"));
        var projPath = Path.Combine(basePath, "0.bproj");

        BeutlApplication app = BeutlApplication.Current;

        var proj = CoreSerializer.RestoreFromUri<Project>(UriHelper.CreateFromPath(projPath));
        app.Project = proj;

        Assert.That(proj.Items.Count, Is.EqualTo(1));
        var scene = proj.Items[0] as Scene;
        Assert.That(scene, Is.Not.Null);
        Assert.That(scene!.Children.Count, Is.EqualTo(1));
        var elm1 = scene.Children[0] as Element;
        Assert.That(elm1, Is.Not.Null);
    }

    // 子要素を持たない Scene を使うこと。子があると子の StoreToUri が先にディレクトリを
    // 作ってしまい、SaveObjectToFile 自身のディレクトリ作成責務を検証できなくなる。
    [Test]
    public void SaveReferencedObjects_CreatesMissingParentDirectories()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"beutl-serialize-mkdir_{Guid.NewGuid():N}");
        try
        {
            var projPath = Path.Combine(baseDir, "project.bproj");
            // scene0 サブディレクトリは事前に作成しない。
            var scenePath = Path.Combine(baseDir, "scene0", "scene0.scene");

            BeutlApplication app = BeutlApplication.Current;
            var proj = new Project { Uri = UriHelper.CreateFromPath(projPath) };
            var scene = new Scene { Uri = UriHelper.CreateFromPath(scenePath) };
            proj.Items.Add(scene);
            app.Project = proj;

            CoreSerializer.StoreToUri(proj, proj.Uri, CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(projPath), Is.True);
                Assert.That(File.Exists(scenePath), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
        }
    }

    // EmbedReferencedObjectsのテスト
    [Test]
    public void SerializeWithEmbedReferencedObjects()
    {
        var basePath = Path.GetFullPath(ArtifactProvider.GetArtifactDirectory());
        var projPath = Path.Combine(basePath, "0.bproj");
        var scenePath = Path.Combine(basePath, "0", "0.scene");
        var layerPath = Path.Combine(basePath, "0", "1", "0.layer");
        // 既存のファイルを削除
        if (File.Exists(projPath)) File.Delete(projPath);
        if (File.Exists(scenePath)) File.Delete(scenePath);
        if (File.Exists(layerPath)) File.Delete(layerPath);

        BeutlApplication app = BeutlApplication.Current;
        var proj = new Project { Uri = UriHelper.CreateFromPath(projPath) };
        var scene = new Scene { Uri = UriHelper.CreateFromPath(scenePath) };
        var elm1 = new Element { Uri = UriHelper.CreateFromPath(layerPath) };
        scene.AddChild(elm1);
        proj.Items.Add(scene);
        app.Project = proj;

        CoreSerializer.StoreToUri(proj, proj.Uri, CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects);

        // ファイルが存在することを確認
        Assert.That(File.Exists(projPath), Is.True);
        Assert.That(File.Exists(scenePath), Is.False);
        Assert.That(File.Exists(layerPath), Is.False);
    }

    [Test]
    public void DeserializeWithEmbedReferencedObjects()
    {
        SerializeWithEmbedReferencedObjects();

        var basePath = Path.GetFullPath(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "../SerializeWithEmbedReferencedObjects"));
        var projPath = Path.Combine(basePath, "0.bproj");

        BeutlApplication app = BeutlApplication.Current;

        var proj = CoreSerializer.RestoreFromUri<Project>(UriHelper.CreateFromPath(projPath));
        app.Project = proj;

        Assert.That(proj.Items.Count, Is.EqualTo(1));
        var scene = proj.Items[0] as Scene;
        Assert.That(scene, Is.Not.Null);
        Assert.That(scene!.Children.Count, Is.EqualTo(1));
        var elm1 = scene.Children[0] as Element;
        Assert.That(elm1, Is.Not.Null);
    }

    // 1.xで作成されたファイルではScene/Elementに$typeが付かないため、
    // RestoreFromUriで補完されることを確認する。
    [Test]
    public void RestoreFromUri_FillsMissingDiscriminatorForLegacyFiles()
    {
        var basePath = Path.GetFullPath(ArtifactProvider.GetArtifactDirectory());
        var scenePath = Path.Combine(basePath, "legacy.scene");
        var layerPath = Path.Combine(basePath, "legacy.belm");
        if (File.Exists(scenePath)) File.Delete(scenePath);
        if (File.Exists(layerPath)) File.Delete(layerPath);

        // $typeを持たない1.x形式のJSONを書き出す
        File.WriteAllText(scenePath, "{\"Width\":1920,\"Height\":1080}");
        File.WriteAllText(layerPath, "{\"Start\":\"00:00:00\",\"Length\":\"00:00:01\"}");

        var scene = CoreSerializer.RestoreFromUri<ProjectItem>(UriHelper.CreateFromPath(scenePath));
        Assert.That(scene, Is.InstanceOf<Scene>());

        var element = CoreSerializer.RestoreFromUri<Element>(UriHelper.CreateFromPath(layerPath));
        Assert.That(element, Is.InstanceOf<Element>());
    }

    [Test]
    public void Resolve()
    {
        var original1 = new TestSerializable();
        var original2 = new TestSerializable();
        var original3 = new TestSerializable();
        original1.Instance = original2;
        original2.Reference = original3;
        original3.Instance = original1;
        var json = new JsonObject();

        var context1 = new JsonSerializationContext(original3.GetType(), json: json);
        using (ThreadLocalSerializationContext.Enter(context1))
        {
            original3.Serialize(context1);
        }

        var restored = new TestSerializable();
        var context2 = new JsonSerializationContext(original3.GetType(), json: json);
        using (ThreadLocalSerializationContext.Enter(context2))
        {
            restored.Deserialize(context2);
            context2.AfterDeserialized(restored);
        }

        Assert.That(restored.Id, Is.EqualTo(original3.Id));
        Assert.That(restored.Instance, Is.Not.Null);
        Assert.That(restored.Instance!.Id, Is.EqualTo(original1.Id));
        Assert.That(restored.Instance.Reference, Is.Null);
        Assert.That(restored.Instance.Instance, Is.Not.Null);
        Assert.That(restored.Instance.Instance!.Id, Is.EqualTo(original2.Id));
        Assert.That(restored.Instance.Instance.Reference, Is.Not.Null);
        Assert.That(restored.Instance.Instance.Reference!.Id, Is.EqualTo(original3.Id));
    }
}
