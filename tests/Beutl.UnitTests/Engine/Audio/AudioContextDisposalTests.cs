using Beutl.Audio;
using Beutl.Audio.Graph;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioContextDisposalTests
{
    [Test]
    public void Dispose_BlocksNodeCleanupFromRepopulatingContext()
    {
        var context = new AudioContext(48_000, 2);
        var node = context.AddNode(new ReentrantCleanupNode(context));

        context.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(context.IsDisposed, Is.True);
            Assert.That(context.Nodes, Is.Empty);
            Assert.That(node.ReentryFailure, Is.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void Clear_BlocksNodeCleanupFromRepopulatingContext()
    {
        using var context = new AudioContext(48_000, 2);
        var node = context.AddNode(new ReentrantCleanupNode(context));

        context.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(context.IsDisposed, Is.False);
            Assert.That(context.Nodes, Is.Empty);
            Assert.That(node.ReentryFailure, Is.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void EndUpdate_BlocksRetiredNodeCleanupFromRepopulatingContext()
    {
        using var context = new AudioContext(48_000, 2);
        var retired = new ReentrantCleanupNode(context);
        context.BeginUpdate([retired]);

        context.EndUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(context.Nodes, Is.Empty);
            Assert.That(retired.ReentryFailure, Is.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void Dispose_AllowsNodeCleanupToReenterDisposeIdempotently()
    {
        var context = new AudioContext(48_000, 2);
        var node = context.AddNode(new ReentrantDisposeNode(context));

        Assert.DoesNotThrow(context.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(context.IsDisposed, Is.True);
            Assert.That(context.Nodes, Is.Empty);
            Assert.That(node.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Clear_RejectsReentrantDisposeWithoutDisposingContext()
    {
        var context = new AudioContext(48_000, 2);
        var node = context.AddNode(new ReentrantDisposeNode(context));
        try
        {
            Assert.Throws<InvalidOperationException>(context.Clear);

            Assert.Multiple(() =>
            {
                Assert.That(context.IsDisposed, Is.False);
                Assert.That(context.Nodes, Is.Empty);
                Assert.That(node.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            context.Dispose();
        }
    }

    [Test]
    public void EndUpdate_RejectsRetiredNodeReentrantDisposeWithoutDestroyingCurrentGraph()
    {
        var context = new AudioContext(48_000, 2);
        var retired = new ReentrantDisposeNode(context);
        var current = new TrackingNode();
        try
        {
            context.BeginUpdate([retired]);
            context.AddNode(current);
            context.MarkAsOutput(current);

            Assert.Throws<InvalidOperationException>(context.EndUpdate);

            Assert.Multiple(() =>
            {
                Assert.That(context.IsDisposed, Is.False);
                Assert.That(context.Nodes, Is.EqualTo(new[] { current }));
                Assert.That(context.GetOutputNodes(), Is.EqualTo(new[] { current }));
                Assert.That(retired.DisposeCalls, Is.EqualTo(1));
                Assert.That(current.DisposeCalls, Is.Zero,
                    "a rejected reentrant dispose must not destroy the graph being committed");
            });
        }
        finally
        {
            context.Dispose();
        }

        Assert.That(current.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_DeduplicatesPreviousNodesByIdentityBeforeCleanup()
    {
        var node = new TrackingNode();
        using var context = new AudioContext(48_000, 2, [node, node]);

        context.EndUpdate();

        Assert.That(node.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void BeginUpdate_DeduplicatesPreviousNodesByIdentityBeforeCleanup()
    {
        var node = new TrackingNode();
        using var context = new AudioContext(48_000, 2);
        context.BeginUpdate([node, node]);

        context.EndUpdate();

        Assert.That(node.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void EndUpdate_WhenInputDetachmentThrows_RetainsAllOwnershipForDispose()
    {
        var detachmentFailure = new InvalidOperationException("input detachment failure");
        var retired = new ThrowingEqualityNode(detachmentFailure);
        var current = new TrackingNode();
        current.AddInput(retired);
        var context = new AudioContext(48_000, 2);
        context.BeginUpdate([retired]);
        context.AddNode(current);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(context.EndUpdate);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(detachmentFailure));
            Assert.That(retired.DisposeCalls, Is.Zero);
            Assert.That(current.DisposeCalls, Is.Zero);
        });

        Assert.DoesNotThrow(context.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(retired.DisposeCalls, Is.EqualTo(1));
            Assert.That(current.DisposeCalls, Is.EqualTo(1));
        });
    }

    private sealed class ReentrantCleanupNode(AudioContext context) : AudioNode
    {
        public Exception? ReentryFailure { get; private set; }

        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            try
            {
                context.AddNode(new PassiveNode());
            }
            catch (Exception ex)
            {
                ReentryFailure = ex;
            }
        }
    }

    private sealed class PassiveNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());
    }

    private sealed class ReentrantDisposeNode(AudioContext context) : AudioNode
    {
        private bool _didReenter;

        public int DisposeCalls { get; private set; }

        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing || _didReenter)
                return;

            _didReenter = true;
            DisposeCalls++;
            context.Dispose();
        }
    }

    private sealed class TrackingNode : AudioNode
    {
        public int DisposeCalls { get; private set; }

        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                DisposeCalls++;
            }
        }
    }

    private sealed class ThrowingEqualityNode(Exception failure) : AudioNode
    {
        public int DisposeCalls { get; private set; }

        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override bool Equals(object? obj) => throw failure;

        public override int GetHashCode() => base.GetHashCode();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                DisposeCalls++;
            }
        }
    }
}
