using System.Text.Json.Nodes;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.Media;
using Beutl.NodeTree;
using Beutl.NodeTree.Nodes;
using Beutl.NodeTree.Nodes.Geometry;
using Beutl.Operation;
using Beutl.Operators.Source;
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
    
        proj.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.bproj"));
        app.Project = proj;
    
        var scene = new Scene();
        scene.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.scene"));
        proj.Items.Add(scene);
        var elm1 = new Element();
        elm1.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.layer"));
        scene.AddChild(elm1).Do();
        elm1.Operation.Children.Add(new EllipseOperator());
        elm1.Operation.Children.Add(new DecorateOperator());
    
        var elm2 = new Element { ZIndex = 2 };
        elm2.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"1.layer"));
        scene.AddChild(elm2).Do();
        var rectNode = new RectGeometryNode();
        var shapeNode = new GeometryShapeNode();
        var outNode = new OutputNode();
        elm2.NodeTree.Nodes.Add(rectNode);
        elm2.NodeTree.Nodes.Add(shapeNode);
        elm2.NodeTree.Nodes.Add(outNode);
    
        Assert.That(((OutputSocket<Geometry.Resource>)rectNode.Items[0]).TryConnect((InputSocket<Geometry.Resource?>)shapeNode.Items[1]));
        Assert.That(((OutputSocket<GeometryRenderNode>)shapeNode.Items[0]).TryConnect((InputSocket<RenderNode?>)outNode.Items[0]));
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

        var context1 = new JsonSerializationContext(original3.GetType(), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(context1))
        {
            original3.Serialize(context1);
        }

        var restored = new TestSerializable();
        var context2 = new JsonSerializationContext(original3.GetType(), NullSerializationErrorNotifier.Instance, json: json);
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
