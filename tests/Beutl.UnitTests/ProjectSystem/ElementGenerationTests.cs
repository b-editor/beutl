using Beutl.Editor;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public sealed class ElementGenerationTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-element-generation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void AnElementNothingGeneratedSavesWithoutAGenerationKey()
    {
        var element = new Element();

        var json = CoreSerializer.SerializeToJsonObject(element);

        Assert.That(json.ContainsKey(nameof(Element.Generation)), Is.False);
    }

    [Test]
    public void TheGenerationRoundTripsWithItsTakes()
    {
        string first = WriteFile("first.png");
        string second = WriteFile("second.png");
        string input = WriteFile("input.png");
        var element = new Element { Generation = CreateGeneration(input, first, second) };
        Guid activeId = element.Generation!.Takes[1].Id;

        var json = CoreSerializer.SerializeToJsonObject(element);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        ElementGeneration generation = restored.Generation!;
        Assert.Multiple(() =>
        {
            Assert.That(generation.Operation, Is.EqualTo("image.edit.restyle"));
            Assert.That(generation.Parameters, Is.EqualTo("""{"prompt":"watercolor"}"""));
            Assert.That(generation.InputImage?.Uri.LocalPath, Is.EqualTo(input));
            Assert.That(generation.Takes.Select(take => take.Image?.Uri.LocalPath), Is.EqualTo(new[] { first, second }));
            Assert.That(generation.Takes[0].IsOriginal, Is.True);
            Assert.That(generation.Takes[1].Seed, Is.EqualTo(42));
            Assert.That(generation.ActiveTakeId, Is.EqualTo(activeId));
            Assert.That(generation.ActiveTake, Is.SameAs(generation.Takes[1]));
            Assert.That(generation.HierarchicalParent, Is.SameAs(restored));
        });
    }

    [Test]
    public void EveryFileTheGenerationKeepsIsEnumerated()
    {
        string first = WriteFile("first.png");
        string second = WriteFile("second.png");
        string input = WriteFile("input.png");
        ElementGeneration generation = CreateGeneration(input, first, second);

        Assert.That(
            generation.EnumerateFileSources().Select(source => source.Uri.LocalPath),
            Is.EquivalentTo(new[] { input, first, second }));
    }

    [Test]
    public void SwitchingTakesAndAddingTheRecordAreUndoable()
    {
        using var harness = new SceneHistoryHarness("beutl_generation", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(60));
        Element element = harness.AddElement();
        HistoryManager history = harness.History;
        ElementGeneration generation = CreateGeneration(WriteFile("input.png"), WriteFile("a.png"), WriteFile("b.png"));
        Guid firstId = generation.Takes[0].Id;
        Guid secondId = generation.Takes[1].Id;

        element.Generation = generation;
        history.Commit("add generation");
        generation.ActiveTakeId = firstId;
        history.Commit("switch take");

        history.Undo();
        Assert.That(element.Generation?.ActiveTakeId, Is.EqualTo(secondId));
        history.Undo();
        Assert.That(element.Generation, Is.Null);
        history.Redo();
        Assert.That(element.Generation, Is.SameAs(generation));
        history.Redo();
        Assert.That(element.Generation?.ActiveTakeId, Is.EqualTo(firstId));
    }

    private ElementGeneration CreateGeneration(string input, string first, string second)
    {
        var generation = new ElementGeneration
        {
            Operation = "image.edit.restyle",
            Parameters = """{"prompt":"watercolor"}""",
            InputImage = ImageSource.Open(input),
        };
        generation.Takes.Add(new ElementGenerationTake { Image = ImageSource.Open(first), IsOriginal = true });
        var take = new ElementGenerationTake { Image = ImageSource.Open(second), Seed = 42, ModelId = "model" };
        generation.Takes.Add(take);
        generation.ActiveTakeId = take.Id;
        return generation;
    }

    private string WriteFile(string name)
    {
        string path = Path.Combine(_directory, name);
        using (var bitmap = new Beutl.Media.Bitmap(2, 2))
        {
            bitmap.Save(path, Beutl.Graphics.EncodedImageFormat.Png);
        }

        return path;
    }
}
