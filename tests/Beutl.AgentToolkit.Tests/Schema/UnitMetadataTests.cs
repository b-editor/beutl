using System.Numerics;
using System.Text.Json;
using Beutl.AgentToolkit.Schema;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Primitives;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Schema;

public class UnitMetadataTests
{
    [TestCase(typeof(ScaleTransform), nameof(ScaleTransform.Scale), "percent")]
    [TestCase(typeof(ScaleTransform), nameof(ScaleTransform.ScaleX), "percent")]
    [TestCase(typeof(ScaleTransform), nameof(ScaleTransform.ScaleY), "percent")]
    [TestCase(typeof(RotationTransform), nameof(RotationTransform.Rotation), "degrees")]
    [TestCase(typeof(SkewTransform), nameof(SkewTransform.SkewX), "degrees")]
    [TestCase(typeof(SkewTransform), nameof(SkewTransform.SkewY), "degrees")]
    [TestCase(typeof(RectShape), nameof(RectShape.Opacity), "percent")]
    [TestCase(typeof(Cube3D), nameof(Object3D.Scale), "ratio")]
    [TestCase(typeof(Cube3D), nameof(Object3D.Rotation), "degrees")]
    [TestCase(typeof(RectShape), nameof(RectShape.Width), null)]
    public void Schema_reads_declared_and_inherited_units_without_guessing_from_the_name(Type type, string name, string? unit)
    {
        var schema = new SchemaGenerator().Generate(typeFilter: type.Name, includeExamples: false);
        PropertyDescriptor property = schema.Types.Single(item => item.Type == type.FullName)
            .Properties.Single(item => item.Name == name);
        Assert.That(property.Unit, Is.EqualTo(unit));
        var json = JsonSerializer.SerializeToNode(property)!;
        Assert.That((string?)json["Unit"], Is.EqualTo(unit));
    }

    [Test]
    public void Matrix_node_inputs_declare_ratio_and_degree_units_without_changing_their_defaults()
    {
        var scale = new ScaleMatrixNode();
        foreach (InputPort<float> input in new[] { scale.Scale, scale.ScaleX, scale.ScaleY })
        {
            Assert.That(input.Property!.GetAttributes().OfType<UnitAttribute>().Single().Unit, Is.EqualTo(PropertyUnit.Ratio));
            Assert.That(input.Property.GetValue(), Is.EqualTo(1f));
        }
        var rotation = new RotationMatrixNode();
        Assert.That(rotation.Rotation.Property!.GetAttributes().OfType<UnitAttribute>().Single().Unit, Is.EqualTo(PropertyUnit.Degrees));
        Assert.That(rotation.Rotation.Property.GetValue(), Is.Zero);
        var property = new SchemaGenerator().Generate(typeFilter: nameof(ScaleTransform), includeExamples: false)
            .Types.Single(item => item.Type == typeof(ScaleTransform).FullName).Properties.Single(item => item.Name == nameof(ScaleTransform.Scale));
        Assert.That(property.Default, Is.EqualTo(100f));
    }

    [Test]
    public void Units_do_not_rescale_or_serialize_with_model_values()
    {
        var scale = new ScaleTransform { Scale = { Value = 75f } };
        var cube = new Cube3D { Scale = { Value = new Vector3(0.5f, 1.25f, 2f) } };
        var node = new ScaleMatrixNode();
        node.Scale.Property!.SetValue(1.25f);
        var scaleJson = CoreSerializer.SerializeToJsonObject(scale);
        var cubeJson = CoreSerializer.SerializeToJsonObject(cube);
        var nodeJson = CoreSerializer.SerializeToJsonObject(node);
        Assert.That(scaleJson.ToJsonString(), Does.Not.Contain("Unit"));
        Assert.That(cubeJson.ToJsonString(), Does.Not.Contain("Unit"));
        Assert.That(nodeJson.ToJsonString(), Does.Not.Contain("Unit"));
        Assert.That(((ScaleTransform)CoreSerializer.DeserializeFromJsonObject(scaleJson, typeof(ScaleTransform))).Scale.Value, Is.EqualTo(75f));
        Assert.That(((Cube3D)CoreSerializer.DeserializeFromJsonObject(cubeJson, typeof(Cube3D))).Scale.Value, Is.EqualTo(cube.Scale.Value));
        Assert.That(((ScaleMatrixNode)CoreSerializer.DeserializeFromJsonObject(nodeJson, typeof(ScaleMatrixNode))).Scale.Property!.GetValue(), Is.EqualTo(1.25f));
    }
}
