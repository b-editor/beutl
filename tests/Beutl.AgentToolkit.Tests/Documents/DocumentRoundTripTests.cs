using Beutl.AgentToolkit.Documents;

namespace Beutl.AgentToolkit.Tests.Documents;

public class DocumentRoundTripTests
{
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
