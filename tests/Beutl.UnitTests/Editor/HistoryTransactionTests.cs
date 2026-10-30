using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class HistoryTransactionTests
{
    [Test]
    public void RecordingDoesNotRecompareUnchangedOperations()
    {
        var transaction = new HistoryTransaction(1);
        int comparisons = 0;
        for (int i = 0; i < 100; i++)
            transaction.AddOperation(new UnmergeableOperation(() => comparisons++) { SequenceNumber = i });

        Assert.That(transaction.OperationCount, Is.EqualTo(100));
        Assert.That(comparisons, Is.LessThanOrEqualTo(100 * 99 / 2));
    }

    [Test]
    public void InterleavedWritesMergeAndNoopsDisappearWithoutLosingUndoRedo()
    {
        var element = new Element();
        var transaction = new HistoryTransaction(1);
        long sequence = 0;
        Write(nameof(Element.Start), MediaTime.FromSeconds(1), MediaTime.Zero);
        Write(nameof(Element.Length), MediaTime.FromSeconds(10), element.Length.Value);
        Write(nameof(Element.Start), MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        Assert.That(transaction.OperationCount, Is.EqualTo(2));
        Write(nameof(Element.Start), MediaTime.Zero, MediaTime.FromSeconds(2));
        Assert.That(transaction.OperationCount, Is.EqualTo(1));
        Write(nameof(Element.Start), MediaTime.FromSeconds(3), MediaTime.Zero);
        transaction.Revert();
        Assert.That(element.Start.Value, Is.EqualTo(MediaTime.Zero));
        transaction.Apply();
        Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        Assert.That(element.Length.Value, Is.EqualTo(MediaTime.FromSeconds(10)));

        void Write(string property, MediaTime value, MediaTime old)
        {
            var operation = new UpdatePropertyValueOperation<MediaTime>(element, property, value, old) { SequenceNumber = sequence++ };
            operation.Apply();
            transaction.AddOperation(operation);
        }
    }

    private sealed class UnmergeableOperation(Action compared) : ChangeOperation, IMergableChangeOperation
    {
        public override void Apply() { }
        public override void Revert() { }
        public bool TryMerge(ChangeOperation other) { compared(); return false; }
    }
}
