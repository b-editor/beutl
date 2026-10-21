using System.Numerics;
using Beutl.Editor;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Models;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

public class ModelHistoryTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("model-history-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase(0, 1)]
    [TestCase(1, 2)]
    public void Source_undo_redo_preserves_mesh_identity_and_subsequent_edits(int initialMeshes, int replacementMeshes)
    {
        var model = new Model3D();
        ModelSource? original = initialMeshes == 0 ? null : Load("original.obj", initialMeshes);
        model.Source.Value = original;
        if (initialMeshes > 0) model.Children[0].Position.Value = new Vector3(7, 0, 0);
        var originalChildren = model.Children.ToArray();
        var element = new Element(); element.Objects.Add(model);
        using var harness = new HistoryHarness(element);
        HistoryManager history = harness.History;
        ModelSource replacement = Load("replacement.obj", replacementMeshes);

        model.Source.Value = replacement;
        history.Commit("Change model");
        var replacementChildren = model.Children.ToArray();
        Assert.That(replacementChildren, Has.Length.EqualTo(replacementMeshes));
        var material = (PBRMaterial)replacementChildren[0].Material.Value!;
        var originalColor = material.Albedo.Value;
        replacementChildren[0].Position.Value = new Vector3(9, 8, 7);
        material.Albedo.Value = Beutl.Media.Colors.Red;
        history.Commit("Edit mesh");

        Assert.That(history.Undo(), Is.True);
        Assert.That(replacementChildren[0].Position.Value, Is.EqualTo(Vector3.Zero));
        Assert.That(material.Albedo.Value, Is.EqualTo(originalColor));
        Assert.That(history.Undo(), Is.True);
        Assert.That(model.Source.Value, Is.SameAs(original));
        Assert.That(model.Children, Has.Count.EqualTo(initialMeshes));
        if (initialMeshes > 0)
        {
            Assert.That(model.Children[0], Is.SameAs(originalChildren[0]));
            Assert.That(model.Children[0].Position.Value, Is.EqualTo(new Vector3(7, 0, 0)));
        }

        Assert.That(history.Redo(), Is.True);
        Assert.That(model.Source.Value, Is.SameAs(replacement));
        Assert.That(model.Children, Has.Count.EqualTo(replacementMeshes));
        for (int i = 0; i < replacementMeshes; i++)
            Assert.That(model.Children[i], Is.SameAs(replacementChildren[i]));
        Assert.That(history.Redo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(model.Children[0].Position.Value, Is.EqualTo(new Vector3(9, 8, 7)));
            Assert.That(((PBRMaterial)model.Children[0].Material.Value!).Albedo.Value, Is.EqualTo(Beutl.Media.Colors.Red));
            Assert.That(history.UndoCount, Is.EqualTo(2));
            Assert.That(history.RedoCount, Is.Zero);
        });
    }

    [Test]
    public void Uncommitted_source_selection_can_be_rolled_back()
    {
        var model = new Model3D();
        using var harness = new HistoryHarness(model);
        HistoryManager history = harness.History;
        model.Source.Value = Load("model.obj", 2);

        history.Rollback();

        Assert.That(model.Source.Value, Is.Null);
        Assert.That(model.Children, Is.Empty);
        Assert.That(history.CanUndo, Is.False);
    }

    private ModelSource Load(string name, int meshes)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\n" +
                                string.Concat(Enumerable.Range(0, meshes).Select(i => $"o Mesh{i}\nf 1 2 3\n")));
        var source = new ModelSource();
        source.ReadFrom(new Uri(path));
        return source;
    }
}
