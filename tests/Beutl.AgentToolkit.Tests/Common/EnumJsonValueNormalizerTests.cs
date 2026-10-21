using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.AgentToolkit.Tests.Common;

public class EnumJsonValueNormalizerTests
{
    // The core reader reads an object without $type as its concrete declared type (b-editor/beutl#2875), so the
    // enum names in it are normalized for that type too, and the value deserializes.
    [Test]
    public void ObjectWithoutType_IsNormalizedAndReadAsItsConcreteDeclaredType()
    {
        var json = new JsonObject { [nameof(UnsealedAlignedObject.Alignment)] = nameof(AlignmentX.Center) };

        object? value = EnumJsonValueNormalizer.Deserialize(json, typeof(UnsealedAlignedObject));

        Assert.That(value, Is.TypeOf<UnsealedAlignedObject>());
        Assert.That(((UnsealedAlignedObject)value!).Alignment.Value, Is.EqualTo(AlignmentX.Center));
    }

    [SuppressResourceClassGeneration]
    private class UnsealedAlignedObject : EngineObject
    {
        public UnsealedAlignedObject() => ScanProperties<UnsealedAlignedObject>();

        public IExpressionProperty<AlignmentX> Alignment { get; } = Property.CreateWithExpression(AlignmentX.Left);
    }
}
