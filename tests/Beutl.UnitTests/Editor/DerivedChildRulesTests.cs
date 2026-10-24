using System.Numerics;
using Beutl.Audio.Effects;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Graphics3D.Models;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

public class DerivedChildRulesTests
{
    [Test]
    public void Recorded_source_creates_meshes_in_one_transaction_and_replay_keeps_their_identity()
    {
        string directory = Directory.CreateTempSubdirectory("derived-child-rules-").FullName;
        try
        {
            string path = Path.Combine(directory, "model.obj");
            File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\no First\nf 1 2 3\no Second\nf 1 2 3\n");
            var source = new ModelSource();
            source.ReadFrom(new Uri(path));
            var model = new Model3D();
            model.Source.Value = source;
            Assert.That(model.Children, Is.Empty, "An unrecorded write must not generate children.");
            model.Source.Value = null;

            var element = new Element();
            element.Objects.Add(model);
            using var harness = new HistoryHarness(element);
            HistoryManager history = harness.History;
            history.ExecuteInTransaction(() => model.Source.Value = source, "Import model");

            var children = model.Children.ToArray();
            var ids = children.Select(child => child.Id.Value).ToArray();
            Assert.That(children, Has.Length.EqualTo(2));
            var operations = history.GetLatestCommittedOperations(history.Entries[^1].TransactionId);
            Assert.That(operations[0], Is.TypeOf<UpdatePropertyValueOperation<ModelSource?>>());
            Assert.That(operations.Skip(1).All(operation => operation is ICollectionChangeOperation), Is.True);
            Assert.That(operations, Has.Count.EqualTo(3));
            Assert.That(history.UndoCount, Is.EqualTo(1));

            children[0].Position.Value = new Vector3(9, 8, 7);
            history.Commit("Move mesh");
            Assert.That(history.Undo(), Is.True);
            Assert.That(children[0].Position.Value, Is.EqualTo(Vector3.Zero));
            Assert.That(history.Undo(), Is.True);
            Assert.That(model.Source.Value, Is.Null);
            Assert.That(model.Children, Is.Empty);

            Assert.That(history.Redo(), Is.True);
            Assert.That(model.Source.Value, Is.SameAs(source));
            Assert.That(model.Children, Is.EqualTo(children));
            Assert.That(model.Children.Select(child => child.Id.Value), Is.EqualTo(ids));
            Assert.That(history.Redo(), Is.True);
            Assert.That(model.Children[0].Position.Value, Is.EqualTo(new Vector3(9, 8, 7)));

            model.Source.Value = null;
            Assert.That(model.Children, Is.Empty);
            history.Rollback();
            Assert.That(model.Source.Value, Is.SameAs(source));
            Assert.That(model.Children, Is.EqualTo(children));
            Assert.That(history.HasPendingOperations, Is.False);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Recorded_band_count_keeps_generated_bands_in_the_edit_session()
    {
        var equalizer = new EqualizerEffect();
        var original = equalizer.Bands.ToArray();
        original[0].Gain.Value = 3f;
        equalizer.BandCountOption.Value = BandCountPreset.Bands5;
        Assert.That(equalizer.Bands, Is.EqualTo(original), "An unrecorded write must not regenerate bands.");
        equalizer.BandCountOption.Value = BandCountPreset.Bands10;

        using var harness = new HistoryHarness(equalizer);
        HistoryManager history = harness.History;
        using (EditSession session = history.BeginEdit("Change band count"))
        {
            using (session.Enter())
            {
                equalizer.BandCountOption.Value = BandCountPreset.Bands15;
                equalizer.BandCountOption.Value = BandCountPreset.Bands5;
            }
            session.Complete();
        }

        var bands = equalizer.Bands.ToArray();
        var ids = bands.Select(band => band.Id.Value).ToArray();
        Assert.That(bands.Select(band => band.Frequency.Value), Is.EqualTo(new[] { 60f, 230f, 910f, 3600f, 14000f }));
        Assert.That(history.UndoCount, Is.EqualTo(1));
        Assert.That(history.HasPendingOperations, Is.False);
        var operations = history.GetLatestCommittedOperations(history.Entries[^1].TransactionId);
        Assert.That(operations[0], Is.TypeOf<UpdatePropertyValueOperation<BandCountPreset>>());
        Assert.That(operations.Skip(1).All(operation => operation is ICollectionChangeOperation), Is.True);

        Assert.That(history.Undo(), Is.True);
        Assert.That(equalizer.BandCountOption.Value, Is.EqualTo(BandCountPreset.Bands10));
        Assert.That(equalizer.Bands, Is.EqualTo(original));
        Assert.That(equalizer.Bands[0].Gain.Value, Is.EqualTo(3f));
        Assert.That(history.Redo(), Is.True);
        Assert.That(equalizer.BandCountOption.Value, Is.EqualTo(BandCountPreset.Bands5));
        Assert.That(equalizer.Bands, Is.EqualTo(bands));
        Assert.That(equalizer.Bands.Select(band => band.Id.Value), Is.EqualTo(ids));

        using (EditSession session = history.BeginEdit("Cancel band count"))
        {
            using (session.Enter())
                equalizer.BandCountOption.Value = BandCountPreset.Bands31;
            session.Cancel();
        }
        Assert.That(equalizer.BandCountOption.Value, Is.EqualTo(BandCountPreset.Bands5));
        Assert.That(equalizer.Bands, Is.EqualTo(bands));
        Assert.That(history.UndoCount, Is.EqualTo(1));
        Assert.That(history.HasPendingOperations, Is.False);
    }
}
