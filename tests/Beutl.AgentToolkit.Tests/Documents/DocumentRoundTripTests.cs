using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Audio.Effects;

namespace Beutl.AgentToolkit.Tests.Documents;

public class DocumentRoundTripTests
{
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
