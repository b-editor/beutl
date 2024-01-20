using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeTree;
using Beutl.NodeTree.Nodes;
using Beutl.NodeTree.Nodes.Geometry;
using Beutl.Operators.Configure.Transform;
using Beutl.Operators.Source;
using Beutl.ProjectSystem;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Beutl.Core.UnitTests;

public class JsonSerializationTest
{
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
        elm1.Operation.Children.Add(new TranslateOperator());

        var elm2 = new Element { ZIndex = 2 };
        elm2.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"1.layer"));
        scene.AddChild(elm2).Do();
        var rectNode = new RectGeometryNode();
        var shapeNode = new GeometryShapeNode();
        var outNode = new LayerOutputNode();
        elm2.NodeTree.Nodes.Add(rectNode);
        elm2.NodeTree.Nodes.Add(shapeNode);
        elm2.NodeTree.Nodes.Add(outNode);

        Assert.That(((OutputSocket<RectGeometry>)rectNode.Items[0]).TryConnect((InputSocket<Geometry?>)shapeNode.Items[1]));
        Assert.That(((OutputSocket<GeometryShape>)shapeNode.Items[0]).TryConnect((InputSocket<Drawable?>)outNode.Items[0]));
    }
}
