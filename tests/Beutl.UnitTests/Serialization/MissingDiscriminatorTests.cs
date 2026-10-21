using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Serialization;

// $type is written only for a declared type that is not sealed, so a file written while a type was sealed holds its
// values without one. Such a value is read as its declared type when that type is concrete, so the type can be
// unsealed later (b-editor/beutl#2875). UnsealedLeaf stands in for a type such as Pen after it is unsealed: it
// inherits EngineObject's fallback, which cannot take its place.
public class MissingDiscriminatorTests
{
    // An engine property value, read through Optional<T> and CoreSerializer.DeserializeFromJsonObject. Read as the
    // inherited fallback, it failed its owner, which became a fallback itself.
    [Test]
    public void EnginePropertyValueWithoutType_IsReadAsTheDeclaredType()
    {
        var holder = new LeafHolder();
        holder.Leaf.Value = CreateLeaf(3);
        JsonObject json = CoreSerializer.SerializeToJsonObject(holder);
        RemoveType(json[nameof(LeafHolder.Leaf)]!.AsObject());
        using DeserializationIncidents.Capture incidents = DeserializationIncidents.BeginCapture();

        object restored = CoreSerializer.DeserializeFromJsonObject(json, typeof(LeafHolder));

        Assert.That(restored, Is.TypeOf<LeafHolder>());
        UnsealedLeaf? leaf = ((LeafHolder)restored).Leaf.Value;
        Assert.That(leaf, Is.TypeOf<UnsealedLeaf>());
        Assert.That(leaf!.Width.Value, Is.EqualTo(3));
        Assert.That(incidents.Count, Is.Zero);
    }

    // A value read through ICoreSerializationContext.GetValue.
    [Test]
    public void ContextValueWithoutType_IsReadAsTheDeclaredType()
    {
        var holder = new CoreHolder { Leaf = CreateLeaf(3) };
        JsonObject json = CoreSerializer.SerializeToJsonObject(holder);
        RemoveType(json[nameof(CoreHolder.Leaf)]!.AsObject());

        var restored = (CoreHolder)CoreSerializer.DeserializeFromJsonObject(json, typeof(CoreHolder));

        Assert.That(restored.Leaf, Is.TypeOf<UnsealedLeaf>());
        Assert.That(restored.Leaf!.Width.Value, Is.EqualTo(3));
    }

    // A file read through CoreSerializer.RestoreFromUri.
    [Test]
    public void FileWithoutType_IsReadAsTheDeclaredType()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-missing-type-{Guid.NewGuid():N}.json");
        try
        {
            JsonObject json = CoreSerializer.SerializeToJsonObject(CreateLeaf(3));
            RemoveType(json);
            File.WriteAllText(path, json.ToJsonString());

            object restored = CoreSerializer.RestoreFromUri(UriHelper.CreateFromPath(path), typeof(UnsealedLeaf));

            Assert.That(restored, Is.TypeOf<UnsealedLeaf>());
            Assert.That(((UnsealedLeaf)restored).Width.Value, Is.EqualTo(3));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ClipboardJsonWithoutType_IsPastedAsTheDeclaredType()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(CreateLeaf(3));
        RemoveType(json);

        bool pasted = CoreObjectClipboard.TryDeserializeJson(json.ToJsonString(), out UnsealedLeaf? leaf);

        Assert.That(pasted, Is.True);
        Assert.That(leaf, Is.TypeOf<UnsealedLeaf>());
        Assert.That(leaf!.Width.Value, Is.EqualTo(3));
    }

    // The engine property's Optional<T> is a sealed wrapper whose node carries its value's $type, so this also
    // shows that the wrapper still ignores that $type and hands it to the value.
    [Test]
    public void PresentType_StillSelectsTheSubtype()
    {
        var engineHolder = new LeafHolder();
        engineHolder.Leaf.Value = new DerivedLeaf();
        var contextHolder = new CoreHolder { Leaf = new DerivedLeaf() };

        var restoredEngineHolder = (LeafHolder)CoreSerializer.DeserializeFromJsonObject(
            CoreSerializer.SerializeToJsonObject(engineHolder), typeof(LeafHolder));
        var restoredContextHolder = (CoreHolder)CoreSerializer.DeserializeFromJsonObject(
            CoreSerializer.SerializeToJsonObject(contextHolder), typeof(CoreHolder));

        Assert.That(restoredEngineHolder.Leaf.Value, Is.TypeOf<DerivedLeaf>());
        Assert.That(restoredContextHolder.Leaf, Is.TypeOf<DerivedLeaf>());
    }

    [Test]
    public void SealedDeclaredType_StillIgnoresAPresentType()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new SealedLeaf());
        json["$type"] = TypeFormat.ToString(typeof(UnsealedLeaf));

        object restored = CoreSerializer.DeserializeFromJsonObject(json, typeof(SealedLeaf));

        Assert.That(restored, Is.TypeOf<SealedLeaf>());
    }

    [Test]
    public void AbstractDeclaredTypeWithoutType_IsStillReadAsItsFallback()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new RectShape());
        RemoveType(json);
        JsonObject expected = json.DeepClone().AsObject();

        object restored = CoreSerializer.DeserializeFromJsonObject(json, typeof(Drawable));

        Assert.That(restored, Is.TypeOf<FallbackDrawable>());
        Assert.That(JsonNode.DeepEquals(((FallbackDrawable)restored).Json, expected), Is.True);
    }

    [Test]
    public void InterfaceDeclaredTypeWithoutType_StillCannotBeRead()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(CreateLeaf(3));
        RemoveType(json);

        Assert.Throws<InvalidOperationException>(() => CoreSerializer.DeserializeFromJsonObject(json, typeof(ILeaf)));
    }

    // EngineObject is concrete, but its own fallback can take its place: an object without $type stays that
    // fallback, which writes it back unchanged, instead of becoming a bare EngineObject that drops its members.
    [Test]
    public void ElementObjectWithoutType_StaysAFallbackThatKeepsIt()
    {
        var element = new Element();
        element.Objects.Add(CreateLeaf(3));
        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        JsonObject item = json[nameof(Element.Objects)]!.AsArray()[0]!.AsObject();
        RemoveType(item);
        JsonObject expected = item.DeepClone().AsObject();

        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        Assert.That(restored.Objects.Single(), Is.TypeOf<FallbackEngineObject>());
        Assert.That(JsonNode.DeepEquals(((FallbackEngineObject)restored.Objects[0]).Json, expected), Is.True);
    }

    private static UnsealedLeaf CreateLeaf(float width)
    {
        var leaf = new UnsealedLeaf();
        leaf.Width.Value = width;
        return leaf;
    }

    // The writer leaves $type out only for a sealed declared type, so this is what it wrote while the type was sealed.
    private static void RemoveType(JsonObject node)
    {
        Assert.That(node.Remove("$type"), Is.True, "The node must have been written with $type, or the test proves nothing.");
    }

    private interface ILeaf : ICoreSerializable;

    [SuppressResourceClassGeneration]
    private class UnsealedLeaf : EngineObject, ILeaf
    {
        public UnsealedLeaf() => ScanProperties<UnsealedLeaf>();

        public IExpressionProperty<float> Width { get; } = Property.CreateWithExpression(1f);
    }

    [SuppressResourceClassGeneration]
    private sealed class DerivedLeaf : UnsealedLeaf;

    [SuppressResourceClassGeneration]
    private sealed class SealedLeaf : EngineObject;

    [SuppressResourceClassGeneration]
    private sealed class LeafHolder : EngineObject
    {
        public LeafHolder() => ScanProperties<LeafHolder>();

        public IExpressionProperty<UnsealedLeaf?> Leaf { get; } = Property.CreateWithExpression<UnsealedLeaf?>();
    }

    private sealed class CoreHolder : CoreObject
    {
        public UnsealedLeaf? Leaf { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Leaf), Leaf);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Leaf = context.GetValue<UnsealedLeaf>(nameof(Leaf));
        }
    }
}
