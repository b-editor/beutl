using Beutl.AgentToolkit.Sessions;

namespace Beutl.AgentToolkit.Tests.Sessions;

public class RecordingPipelineTests
{
    [Test]
    public void Create_RecordsOneUndoableTransactionForLiveMutation()
    {
        var root = new TestModel();
        using RecordingPipeline pipeline = RecordingPipeline.Create(root);

        pipeline.History.ExecuteInTransaction(() => root.Value = 42, "agent edit");

        Assert.That(pipeline.History.CanUndo, Is.True);
        Assert.That(root.Value, Is.EqualTo(42));

        pipeline.History.Undo();

        Assert.That(root.Value, Is.EqualTo(0));
    }

    [Test]
    public void ExecuteInTransaction_KeepsPriorPendingMutationSeparate()
    {
        var root = new TestModel();
        using RecordingPipeline pipeline = RecordingPipeline.Create(root);
        root.Value = 1;

        pipeline.History.ExecuteInTransaction(() => root.Value = 2, "agent edit");

        Assert.That(pipeline.History.UndoCount, Is.EqualTo(2));
        Assert.That(pipeline.History.Undo(), Is.True);
        Assert.That(root.Value, Is.EqualTo(1));
        Assert.That(pipeline.History.Undo(), Is.True);
        Assert.That(root.Value, Is.Zero);
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
