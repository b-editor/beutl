using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Graphics3D.Meshes;
using Beutl.Graphics3D.Models;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Serialization;

public class CoreSerializerVisitorTests
{
    [Test]
    public void VisitsSavedMembersWrappersAndReferencesWithoutFollowingReferenceTargets()
    {
        var target = new SavedObject();
        var file = new TestFileSource(new Uri("file:///visitor/media.png"));
        var root = new SavedObject
        {
            Saved = new Dictionary<string, object>
            {
                ["optional"] = new Optional<SavedObject>(target),
                ["record"] = new Wrapper(file, new SavedObject()),
                ["reference"] = new Reference<SavedObject>(new SavedObject()),
            },
        };
        var visits = Collect(root);

        Assert.Multiple(() =>
        {
            Assert.That(visits.Single(v => ReferenceEquals(v.Value, target)).Path, Is.EqualTo("$/Saved/optional"));
            Assert.That(visits.Single(v => ReferenceEquals(v.Value, file)).Path, Is.EqualTo("$/Saved/record/Value"));
            Assert.That(visits.Select(v => v.Value).OfType<IReference>().Count(), Is.EqualTo(1));
            Assert.That(visits.Select(v => v.Value).OfType<SavedObject>().Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public void ConvertedCollectionsStayOpaqueAndDoNotInvokeTheirJsonWriter()
    {
        var root = new SavedObject { Saved = new OpaqueItems { new SavedObject() } };

        Assert.That(Collect(root).Select(v => v.Value).OfType<SavedObject>(), Is.EqualTo(new[] { root }));
    }

    [Test]
    public void ScalarCollectionsAreNotEnumerated()
    {
        object[] collections =
        [
            new UnenumerableItems<uint>(), new UnenumerableItems<float>(),
            new UnenumerableItems<decimal>(), new UnenumerableItems<uint?>(),
            new UnenumerableItems<Vertex3D>(),
        ];

        Assert.That(Collect(collections).Where(v => v.Index is not null).Select(v => v.Value),
            Is.EqualTo(collections));
    }

    [Test]
    public void MeshBuffersAndNumericDictionaryValuesAreNotVisited()
    {
        var mesh = new ModelMesh
        {
            Indices = { Value = [0, 1, 2] },
            Vertices = { Value = [default, default, default] },
        };
        var root = new SavedObject { Saved = new object[] { mesh, new Dictionary<string, uint> { ["count"] = 3 } } };
        var visits = Collect(root);

        Assert.Multiple(() =>
        {
            Assert.That(visits.Select(v => v.Value).OfType<ModelMesh>(), Is.EqualTo(new[] { mesh }));
            Assert.That(visits.Any(v => v.Value is uint or Vertex3D), Is.False);
        });
    }

    [Test]
    public void CollectionsOfOptionalAndStructuredValuesStillReachResources()
    {
        var file = new TestFileSource(new Uri("file:///visitor/media.png"));
        object[] collections =
        [
            new Optional<TestFileSource>[] { new(file) },
            new ResourceValue[] { new(file) },
            new Dictionary<string, ResourceValue> { ["asset"] = new(file) },
            new Dictionary<int, TestFileSource> { [7] = file },
        ];

        Assert.That(Collect(collections).Where(v => ReferenceEquals(v.Value, file)).Select(v => v.Path),
            Is.EqualTo(new[] { "$[0][0]", "$[1][0]/Value", "$[2]/asset/Value", "$[3][0]/Value" }));
    }

    [Test]
    public void SharedValuesAreReportedAtBothMembersAndCyclesStop()
    {
        var child = new SavedObject();
        var root = new SavedObject { Saved = new[] { child, child } };
        child.Saved = root;

        var visits = Collect(root);

        Assert.That(visits.Where(v => ReferenceEquals(v.Value, child)).Select(v => v.Path),
            Is.EqualTo(new[] { "$/Saved[0]", "$/Saved[1]" }));
        Assert.That(root.Writes, Is.EqualTo(1));
        Assert.That(child.Writes, Is.EqualTo(1));
    }

    [Test]
    public void VisitsFileBackedObjectsWithoutWritingFilesAndRestoresAmbientContextOnEarlyExit()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"beutl_visit_{Guid.NewGuid():N}");
        var child = new SavedObject { Uri = new Uri(Path.Combine(directory, "child.json")) };
        var root = new SavedObject { Saved = child };
        var ambient = new JsonSerializationContext(typeof(SavedObject));
        using (ThreadLocalSerializationContext.Enter(ambient))
        {
            Assert.That(CoreSerializer.Visit(root, visit => ReferenceEquals(visit.Value, child)), Is.True);
            Assert.That(ThreadLocalSerializationContext.Current, Is.SameAs(ambient));
        }

        Assert.That(Directory.Exists(directory), Is.False);
        Assert.That(child.Writes, Is.Zero);
        Collect(root);
        Assert.That(child.Writes, Is.EqualTo(1));
        Assert.That(Directory.Exists(directory), Is.False);
    }

    private static List<CoreSerializationVisit> Collect(object root)
    {
        var visits = new List<CoreSerializationVisit>();
        CoreSerializer.Visit(root, visit => { visits.Add(visit); return false; });
        return visits;
    }

    private sealed record Wrapper(object Value, [property: JsonIgnore] object Ignored);

    private readonly record struct ResourceValue(TestFileSource Value);

    private sealed class UnenumerableItems<T> : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("Scalar items must not be enumerated.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [JsonConverter(typeof(OpaqueConverter))]
    private sealed class OpaqueItems : List<SavedObject>;

    private sealed class OpaqueConverter : JsonConverter<OpaqueItems>
    {
        public override OpaqueItems Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, OpaqueItems value, JsonSerializerOptions options)
            => throw new InvalidOperationException("The visitor must not write JSON.");
    }

    private sealed class SavedObject : CoreObject
    {
        public object? Saved { get; set; }
        public int Writes { get; private set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            Writes++;
            base.Serialize(context);
            context.SetValue(nameof(Saved), Saved);
        }
    }
}
