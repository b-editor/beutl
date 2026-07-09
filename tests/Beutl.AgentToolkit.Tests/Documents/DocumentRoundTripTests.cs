using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Documents;

public class DocumentRoundTripTests
{
    [Test]
    public void Write_with_a_present_non_object_animations_map_is_rejected_without_clearing_animations()
    {
        var text = new TextBlock { Text = { CurrentValue = "Title" } };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0 }, out _);
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 100 }, out _);
        text.Opacity.Animation = animation;
        var adapter = new DocumentAdapter();

        JsonObject document = adapter.Read(text);
        // A present-but-non-object Animations map is malformed, not an omission; it must be rejected
        // instead of clearing every animatable property's animation.
        document["Animations"] = new JsonArray();

        Assert.Throws<ReconcileException>(() => adapter.Write(text, document));
        Assert.That(text.Opacity.Animation, Is.SameAs(animation));
    }

    [Test]
    public void Write_with_a_present_non_array_child_list_is_rejected_without_clearing_the_list()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        scene.Children.Add(new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(Path.Combine(dir, "first.belm")) });
        var adapter = new DocumentAdapter();

        JsonObject document = adapter.Read(scene);
        // A present-but-non-array Elements is a malformed document, not an intentional omission; it
        // must be rejected instead of falling through to the clear branch and erasing the timeline.
        document["Elements"] = new JsonObject();

        Assert.Throws<ReconcileException>(() => adapter.Write(scene, document));
        Assert.That(scene.Children, Has.Count.EqualTo(1));
    }

    [Test]
    public void Write_with_an_invalid_typed_list_member_leaves_the_original_list_intact()
    {
        var equalizer = new EqualizerEffect();
        int originalBandCount = equalizer.Bands.Count;
        Assert.That(originalBandCount, Is.GreaterThan(0));
        var adapter = new DocumentAdapter();

        JsonObject document = adapter.Read(equalizer);
        // A non-object, no-Id entry forces the wholesale ReplaceList path and fails validation; the
        // replacement must be rejected without first clearing the existing bands (Write can run
        // outside a HistoryManager transaction, so there is no rollback).
        document[nameof(EqualizerEffect.Bands)] = new JsonArray(JsonValue.Create(42));

        Assert.Throws<ReconcileException>(() => adapter.Write(equalizer, document));
        Assert.That(equalizer.Bands, Has.Count.EqualTo(originalBandCount));
    }

    [Test]
    public void WriteThenRead_EmitsOnlyCurrentSerializedContent()
    {
        var root = new TestModel { Value = 3 };
        var adapter = new DocumentAdapter();

        var document = adapter.Read(root);
        document["FutureField"] = "kept";

        adapter.Write(root, document);
        var reread = adapter.Read(root);

        Assert.Multiple(() =>
        {
            Assert.That(root.Value, Is.EqualTo(3));
            Assert.That(reread.ContainsKey("FutureField"), Is.False);
        });
    }

    private sealed class TestModel : CoreObject
    {
        public static readonly CoreProperty<int> ValueProperty =
            ConfigureProperty<int, TestModel>(nameof(Value))
                .DefaultValue(0)
                .Register();

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
