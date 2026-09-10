using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class ReferencedSidecarSaveTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-sidecar-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [TestCase(false)]
    [TestCase(true)]
    public void SelfReference_PreservesTheUriWithoutReenteringItsSave(bool converter)
    {
        var node = Create("self", converter);
        node.Next = node;

        CoreSerializer.StoreToUri(node, node.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(node.SerializeCalls, Is.EqualTo(1));
            Assert.That(new Uri(node.Uri!, Read(node)["Next"]!.GetValue<string>()), Is.EqualTo(node.Uri));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MutualReferences_StillSaveLeavesAfterTheBackEdge(bool converter)
    {
        var first = Create("first", converter);
        var second = Create("second", converter);
        var leaf = Create("leaf", converter);
        first.Next = second;
        second.Next = first;
        second.Other = leaf;
        leaf.Value = 42;

        CoreSerializer.StoreToUri(first, first.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(first.SerializeCalls, Is.EqualTo(1));
            Assert.That(second.SerializeCalls, Is.EqualTo(1));
            Assert.That(Read(first)["Next"]!.GetValue<string>(), Is.EqualTo("second.json"));
            Assert.That(Read(second)["Next"]!.GetValue<string>(), Is.EqualTo("first.json"));
            Assert.That(Read(second)["Other"]!.GetValue<string>(), Is.EqualTo("leaf.json"));
            Assert.That(Read(leaf)["Value"]!.GetValue<int>(), Is.EqualTo(42));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SharedDagLeaf_AndSubsequentSavesArePersisted(bool converter)
    {
        var root = Create("root", converter);
        var left = Create("left", converter);
        var right = Create("right", converter);
        var leaf = Create("leaf", converter);
        root.Next = left;
        root.Other = right;
        left.Next = right.Next = leaf;
        leaf.Value = 1;
        CoreSerializer.StoreToUri(root, root.Uri!);
        Assert.That(Read(leaf)["Value"]!.GetValue<int>(), Is.EqualTo(1));

        leaf.Value = 2;
        CoreSerializer.StoreToUri(root, root.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(Read(leaf)["Value"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(Read(left)["Next"]!.GetValue<string>(), Is.EqualTo("leaf.json"));
            Assert.That(Read(right)["Next"]!.GetValue<string>(), Is.EqualTo("leaf.json"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedCyclicSave_KeepsExistingFileAndAllowsRetry(bool converter)
    {
        var first = Create("first", converter);
        var second = Create("second", converter);
        first.Next = second;
        second.Next = first;
        first.ThrowAfterReferences = true;
        File.WriteAllText(first.Uri!.LocalPath, "existing bytes");

        var failure = Assert.Throws<InvalidOperationException>(() => CoreSerializer.StoreToUri(first, first.Uri));
        Assert.That(failure!.Message, Is.EqualTo("Injected save failure."));
        Assert.That(File.ReadAllText(first.Uri.LocalPath), Is.EqualTo("existing bytes"));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);

        first.ThrowAfterReferences = false;
        first.Value = 99;
        CoreSerializer.StoreToUri(first, first.Uri);
        Assert.That(Read(first)["Value"]!.GetValue<int>(), Is.EqualTo(99));
        Assert.That(Read(second)["Next"]!.GetValue<string>(), Is.EqualTo("first.json"));
    }

    [Test]
    public void SerializationOnlyEntryPoint_AlsoTerminatesSidecarCycles()
    {
        var first = Create("first", false);
        var second = Create("second", true);
        first.Next = second;
        second.Next = first;
        JsonObject json = CoreSerializer.SerializeToJsonObject(first, new CoreSerializerOptions
        {
            BaseUri = first.Uri,
            Mode = CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects,
        });
        Assert.That(json["Next"]!.GetValue<string>(), Is.EqualTo("second.json"));
        Assert.That(Read(second)["Next"]!.GetValue<string>(), Is.EqualTo("first.json"));
    }

    private FileNode Create(string name, bool converter)
        => new() { Uri = new Uri(Path.Combine(_directory, name + ".json")), UseConverter = converter };

    private static JsonObject Read(FileNode node)
        => JsonNode.Parse(File.ReadAllText(node.Uri!.LocalPath))!.AsObject();

    private sealed class FileNode : CoreObject
    {
        public FileNode? Next { get; set; }
        public FileNode? Other { get; set; }
        public int Value { get; set; }
        public bool UseConverter { get; set; }
        public bool ThrowAfterReferences { get; set; }
        public int SerializeCalls { get; private set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            // Turn unbounded recursion into an ordinary test failure, not a process-wide
            // StackOverflowException that would kill the rest of the test runner.
            if (++SerializeCalls > 8) throw new InvalidOperationException("Recursive sidecar save did not terminate.");
            base.Serialize(context);
            context.SetValue(nameof(Value), Value);
            WriteReference(nameof(Next), Next);
            WriteReference(nameof(Other), Other);
            if (ThrowAfterReferences) throw new InvalidOperationException("Injected save failure.");

            void WriteReference(string name, FileNode? value)
            {
                if (UseConverter && value is not null)
                    context.SetValue(name, JsonSerializer.SerializeToNode<ICoreSerializable>(value));
                else
                    context.SetValue(name, value);
            }
        }
    }
}
