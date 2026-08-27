using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGeneratorTest;

/// <summary>
/// Pins what BESG005 reports about a render node that changes state its <c>Process</c> reads, and pins the
/// shapes it deliberately stays quiet about.
/// </summary>
/// <remarks>
/// The quiet cases matter as much as the loud one. This rule guards a public extension point, so an author
/// who meets a false positive suppresses the id and loses the real reports with it. Each "is not reported"
/// case below is a bound the rule documents, not an oversight.
/// </remarks>
[TestFixture]
public sealed class RenderNodeChangeMarkingAnalyzerTests
{
    private const string RenderNodeStubs = """
        namespace Beutl.Graphics
        {
            public readonly record struct Rect(float X, float Y, float Width, float Height);
        }

        namespace Beutl.Graphics.Rendering
        {
            using Beutl.Graphics;

            public sealed class RenderNodeContext
            {
                public void Publish(Rect bounds) { }
            }

            public abstract class RenderNode
            {
                private bool _hasChanges;

                public bool HasChanges => _hasChanges;

                public void MarkChanged() => _hasChanges = true;

                internal void ClearChanges(long observedVersion) => _hasChanges = false;

                public abstract void Process(RenderNodeContext context);

                protected virtual void OnDispose(bool disposing) { }
            }
        }
        """;

    [Test]
    public void AMutatorThatDoesNotMarkTheNode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "an Update that changes what Process reads and never marks the node is the case this rule exists for");
    }

    [Test]
    public void AnAutoPropertyMutatedWithoutMarking_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                public Rect Bounds { get; private set; }

                public void Update(Rect bounds) => Bounds = bounds;

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG005"));
    }

    [Test]
    public void AMutatorThatMarksTheNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;

                public bool Update(Rect bounds)
                {
                    if (_bounds == bounds)
                        return false;

                    _bounds = bounds;
                    MarkChanged();
                    return true;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The mark is often factored into a helper, so the rule follows same-type calls to find it. Missing that
    /// would report every node that shares one invalidation routine.
    /// </remarks>
    [Test]
    public void AMutatorThatMarksThroughAHelper_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    Invalidate();
                }

                private void Invalidate() => MarkChanged();

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The successor to the shape this rule used to wave through. Lowering the flag on the very path that
    /// changes what Process reads is the opposite of marking, and the analyzer once accepted it because it
    /// only asked whether <c>HasChanges</c> was written, never what was written to it.
    /// </remarks>
    [Test]
    public void AMutatorThatClearsInsteadOfMarking_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class UnInvalidatingNode : RenderNode
            {
                private Rect _bounds;

                public Rect Bounds
                {
                    get => _bounds;
                    set
                    {
                        _bounds = value;
                        ClearChanges(0);
                    }
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "clearing the flag where the value changes leaves the recording stale, not marked");
    }

    /// <remarks>
    /// Whether this node's own recording went stale is not something marking a different node answers, so a
    /// call that names another instance cannot excuse the assignment.
    /// </remarks>
    [Test]
    public void AMutatorThatMarksAnotherNode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ForwardingNode : RenderNode
            {
                private readonly RenderNode _other;
                private Rect _bounds;

                public ForwardingNode(RenderNode other)
                {
                    _other = other;
                }

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    _other.MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG005"));
    }

    /// <remarks>
    /// The stronger half of the guard, and the reason the rule no longer has to spot an un-marking: a node
    /// outside the engine cannot withdraw a change it already reported, because <c>HasChanges</c> has no
    /// setter to withdraw it through. Only a consumed recording lowers the flag.
    /// </remarks>
    [Test]
    public void AMutatorThatUnMarksTheNode_DoesNotCompile()
    {
        ImmutableArray<Diagnostic> errors = CompileErrors("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class UnMarkingNode : RenderNode
            {
                private Rect _bounds;

                public Rect Bounds
                {
                    get => _bounds;
                    set
                    {
                        _bounds = value;
                        HasChanges = false;
                    }
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            errors.Select(static d => d.Id),
            Does.Contain("CS0200"),
            "an assignment to HasChanges is what BESG005 used to accept as a mark; it no longer binds");
    }

    [Test]
    public void AFieldProcessNeverReads_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class BookkeepingNode : RenderNode
            {
                private Rect _bounds;
                private int _updateCount;

                public void Update(Rect bounds)
                {
                    _updateCount++;
                    if (_bounds != bounds)
                    {
                        _bounds = bounds;
                        MarkChanged();
                    }
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "state no recording depends on cannot make a recording stale");
    }

    /// <remarks>
    /// Teardown runs after the last recording, so nulling a field there has no later frame to invalidate.
    /// Every render node in the tree does it, and reporting them would have made the rule pure noise.
    /// </remarks>
    [Test]
    public void ADisposalOverrideClearingState_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class OwningNode : RenderNode
            {
                private object? _payload = new object();

                public override void Process(RenderNodeContext context)
                {
                    if (_payload is not null)
                        context.Publish(default);
                }

                protected override void OnDispose(bool disposing)
                {
                    _payload = null;
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// This is the rule's largest documented blind spot: a value memoized during recording is written on the
    /// very path that reads it, and nothing in the syntax separates a legitimate memo from a node whose state
    /// drifts every time it records. The runtime cross-check, which has both recordings to compare, is what
    /// covers this half.
    /// </remarks>
    [Test]
    public void AnAssignmentInsideProcess_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class MemoizingNode : RenderNode
            {
                private Rect _bounds;
                private int _recordCount;

                public override void Process(RenderNodeContext context)
                {
                    _recordCount++;
                    _bounds = new Rect(0, 0, _recordCount, 1);
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    [Test]
    public void AConstructorAssignment_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ImmutableNode : RenderNode
            {
                private Rect _bounds;

                public ImmutableNode(Rect bounds)
                {
                    _bounds = bounds;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    [Test]
    public void ATypeThatIsNotARenderNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class NotANode
            {
                private Rect _bounds;

                public void Update(Rect bounds) => _bounds = bounds;

                public void Process(RenderNodeContext context) => context.Publish(_bounds);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// Reading a property whose setter marks is not a mark - the setter never ran. The rule used to walk both
    /// accessors of every property reference, so a bare read of a marking property cleared every assignment
    /// in the mutator that read it.
    /// </remarks>
    [Test]
    public void AMutatorThatOnlyReadsAMarkingProperty_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;
                private int _generation;

                public int Generation
                {
                    get => _generation;
                    set
                    {
                        _generation = value;
                        MarkChanged();
                    }
                }

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    if (Generation > 0)
                        return;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "a read cannot run the setter that marks, so it cannot excuse the assignment beside it");
    }

    /// <remarks>
    /// The other half of the accessor split: a write really does run the marking setter, so factoring the
    /// mark into a property is still a way to invalidate the node.
    /// </remarks>
    [Test]
    public void AMutatorThatWritesThroughAMarkingProperty_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;
                private int _generation;

                public int Generation
                {
                    get => _generation;
                    set
                    {
                        _generation = value;
                        MarkChanged();
                    }
                }

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    Generation = _generation + 1;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A conditional access keeps its receiver in the enclosing expression rather than beside the name, so
    /// <c>_other?.MarkChanged()</c> looks bare to a check that only knows qualified member access.
    /// </remarks>
    [Test]
    public void AMutatorThatConditionallyMarksAnotherNode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ForwardingNode : RenderNode
            {
                private readonly RenderNode _other;
                private Rect _bounds;

                public ForwardingNode(RenderNode other)
                {
                    _other = other;
                }

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    _other?.MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "marking whatever _other happens to be says nothing about this node's own recording");
    }

    /// <remarks>
    /// Reaching the mark through a helper called on another instance is the same forwarding one call deeper:
    /// the helper's body names <c>MarkChanged</c> bare, but the receiver that helper ran against was not this
    /// node.
    /// </remarks>
    [Test]
    public void AMutatorThatMarksAnotherNodeThroughAHelper_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ForwardingNode : RenderNode
            {
                private readonly ForwardingNode _other;
                private Rect _bounds;

                public ForwardingNode(ForwardingNode other)
                {
                    _other = other;
                }

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    _other.Invalidate();
                }

                private void Invalidate() => MarkChanged();

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG005"));
    }

    /// <remarks>
    /// Spelling the receiver out is the ordinary way to write the mark, and the receiver check has to keep
    /// accepting it. The unqualified spelling is pinned by <see cref="AMutatorThatMarksTheNode_IsNotReported"/>.
    /// </remarks>
    [Test]
    public void AMutatorThatMarksThroughThisAndBase_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;
                private int _generation;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    this.MarkChanged();
                }

                public void Advance()
                {
                    _generation++;
                    base.MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                    context.Publish(new Rect(_generation, 0, 0, 0));
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The render pipeline calls <c>Process</c> through the <c>RenderNode</c> slot, so a same-named overload
    /// declared on the node is not the body that records it. Picking the overload leaves the read set empty
    /// and the rule silent about everything the inherited body actually reads.
    /// </remarks>
    [Test]
    public void ADerivedNodeShadowedByAnUnrelatedProcessOverload_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal abstract class PublishingNode : RenderNode
            {
                protected Rect Bounds;

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }

            internal sealed class DriftingNode : PublishingNode
            {
                public void Process(int frameIndex)
                {
                }

                public void Update(Rect bounds)
                {
                    Bounds = bounds;
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "the inherited override is the body that records, whatever else on the node shares its name");
    }

    /// <remarks>
    /// The same shape declared on one type: the overload sits beside the real override rather than above it
    /// in the chain, so member order alone decided which one the rule read.
    /// </remarks>
    [Test]
    public void ANodeDeclaringAnUnrelatedProcessOverloadFirst_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Process(int frameIndex)
                {
                }

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG005"));
    }

    /// <remarks>
    /// Finding the real override must not turn every node carrying an overload into a report: this one marks,
    /// and the inherited body is read only to learn what marking had to cover.
    /// </remarks>
    [Test]
    public void ADerivedNodeWithAnUnrelatedProcessOverloadThatMarks_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal abstract class PublishingNode : RenderNode
            {
                protected Rect Bounds;

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }

            internal sealed class WellBehavedNode : PublishingNode
            {
                public void Process(int frameIndex)
                {
                }

                public void Update(Rect bounds)
                {
                    Bounds = bounds;
                    MarkChanged();
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A property whose accessors have bodies is skipped so the assignment inside the setter is reported
    /// instead, but the field keyword names a backing field no other member can reach. Excluding every field
    /// a property owns dropped both halves and left the state invisible.
    /// </remarks>
    [Test]
    public void ASemiAutoPropertyMutatedWithoutMarking_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                public Rect Bounds
                {
                    get => field;
                    set => field = value;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG005"));
    }

    [Test]
    public void ASemiAutoPropertyThatMarks_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                public Rect Bounds
                {
                    get => field;
                    set
                    {
                        field = value;
                        MarkChanged();
                    }
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The setter of an auto-property is synthesized, so there is no body anywhere for the walk over member
    /// bodies to read, and nobody inside the node writes the property: the shape that reports an assignment
    /// finds nothing to report. The assignment is written by whoever holds the node, which this rule never
    /// sees, and the recording is stale from the moment it lands.
    /// </remarks>
    [Test]
    [TestCase("public Rect Bounds { get; set; }")]
    [TestCase("public Rect Bounds { get; protected set; }")]
    [TestCase("public Rect Bounds { get; internal set; }")]
    public void AnExternallyWritableAutoPropertyProcessReads_IsReported(string declaration)
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal class DriftingNode : RenderNode
            {
                {{declaration}}

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "a setter anyone outside the node can call changes what Process reads with no mark anywhere");
    }

    /// <remarks>
    /// An init accessor can only run while the object is being made, which is before there is a recording
    /// to invalidate - the same reason a constructor assignment is not reported.
    /// </remarks>
    [Test]
    public void AnInitOnlyAutoPropertyProcessReads_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                public Rect Bounds { get; init; }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A private setter is reachable only from the node's own code, which is exactly what the assignment
    /// shape already reads; reporting the declaration too would report the same state twice and reject the
    /// well-behaved node below.
    /// </remarks>
    [Test]
    public void APrivateSetAutoPropertyWrittenWhereAMarkFollows_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                public Rect Bounds { get; private set; }

                public void Update(Rect bounds)
                {
                    Bounds = bounds;
                    MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    [Test]
    public void AGetOnlyAutoPropertyProcessReads_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                public WellBehavedNode(Rect bounds) => Bounds = bounds;

                public Rect Bounds { get; }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The read set is what makes a mutation matter. A property no Process reads can go stale without any
    /// frame noticing, so an externally writable one is not on its own a diagnostic.
    /// </remarks>
    [Test]
    public void AnExternallyWritableAutoPropertyProcessNeverReads_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class QuietNode : RenderNode
            {
                private Rect _drawn;

                public Rect Bounds { get; set; }

                public void Update(Rect bounds)
                {
                    _drawn = bounds;
                    MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_drawn);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The fix the diagnostic recommends, pinned: giving the setter a body puts the mark on the path every
    /// external assignment takes, and the property is then no different from any other marking mutator.
    /// </remarks>
    [Test]
    public void APublicPropertyWhoseSetterMarks_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;

                public Rect Bounds
                {
                    get => _bounds;
                    set
                    {
                        _bounds = value;
                        MarkChanged();
                    }
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// An element write is the assignment shape, not the collection-mutation bound. Nothing but this field
    /// reaches the array, no other type's body runs, and the read side already counts the field as state
    /// Process depends on - reading the write out differently is the asymmetry that made the rule silent
    /// while the node went stale. readonly is the sharpest form of it: the reference cannot be reassigned,
    /// so an element write is the only way the value ever changes.
    /// </remarks>
    [Test]
    public void AMutatorWritingAnElementOfTrackedStateWithoutMarking_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class SlotNode : RenderNode
            {
                private readonly Rect[] _bounds = new Rect[1];

                public void Update(Rect bounds)
                {
                    _bounds[0] = bounds;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds[0]);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "writing an element changes exactly the value Process reads back");
    }

    /// <remarks>
    /// The read side of the same syntax, pinned: a member that only takes an element out changes nothing,
    /// and reporting it would make every accessor over an array field a diagnostic.
    /// </remarks>
    [Test]
    public void AMutatorReadingAnElementOfTrackedState_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class SlotNode : RenderNode
            {
                private readonly Rect[] _bounds = new Rect[1];

                public Rect Peek()
                {
                    Rect bounds = _bounds[0];
                    return bounds;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds[0]);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "taking an element out leaves the node's recording as valid as it was");
    }

    /// <remarks>
    /// An element write is judged by the same mark the assignment shape is judged by, so a node that marks
    /// stays accepted. Without this the fix the diagnostic recommends would not silence it.
    /// </remarks>
    [Test]
    public void AMutatorWritingAnElementAndMarkingTheNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class SlotNode : RenderNode
            {
                private readonly Rect[] _bounds = new Rect[1];

                public void Update(Rect bounds)
                {
                    _bounds[0] = bounds;
                    MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds[0]);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the mark covers an element write as it covers a whole-field assignment");
    }

    private static ImmutableArray<Diagnostic> Analyze(string source)
    {
        CSharpCompilation compilation = CreateCompilation(source);

        // A source that does not bind produces no analyzer diagnostics, which would let a "stays accepted"
        // case pass without the analyzer ever having looked at it.
        Assert.That(
            compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the test source must compile, or the assertions below prove nothing");

        return compilation
            .WithAnalyzers([new RenderNodeChangeMarkingAnalyzer()])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>The compiler errors <paramref name="source"/> produces, for a case that must not bind.</summary>
    private static ImmutableArray<Diagnostic> CompileErrors(string source)
        => [.. CreateCompilation(source).GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error)];

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "AnalyzerTest",
            [
                CSharpSyntaxTree.ParseText(RenderNodeStubs),
                CSharpSyntaxTree.ParseText(source),
            ],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
                .ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
