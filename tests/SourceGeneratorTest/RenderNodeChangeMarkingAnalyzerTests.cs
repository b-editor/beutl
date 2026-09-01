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

                public void PaintedSource(System.Action<Rect> draw) { }
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
            diagnostics.Where(static d => d.GetMessage().Contains("'DriftingNode.Update'")),
            Is.Not.Empty,
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
    /// and the inherited body is read only to learn what marking had to cover. The inherited state is
    /// reached through a setter that marks, which is the fix the declaration shape recommends - a protected
    /// field would be reported where the base declares it, whatever the derived node does with it.
    /// </remarks>
    [Test]
    public void ADerivedNodeWithAnUnrelatedProcessOverloadThatMarks_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal abstract class PublishingNode : RenderNode
            {
                private Rect _bounds;

                protected Rect Bounds
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
    /// The same shape under a different keyword. A field-like event's accessors are synthesized, so there
    /// is no body for the assignment shape to read, and += binds from wherever the event is visible - so a
    /// subscriber added from outside changes what Process reads with nothing inside the node to report.
    /// </remarks>
    [Test]
    [TestCase("public event System.Action Invalidated;")]
    [TestCase("protected event System.Action Invalidated;")]
    [TestCase("internal event System.Action Invalidated;")]
    public void APublicFieldLikeEventProcessReads_IsReported(string declaration)
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal class DriftingNode : RenderNode
            {
                {{declaration}}

                public override void Process(RenderNodeContext context)
                {
                    if (Invalidated is not null)
                        context.Publish(default);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "an event anyone outside the node can subscribe to changes what Process reads with no mark "
            + "anywhere");
    }

    /// <remarks>
    /// A field code outside the node can assign is the same declaration hazard as a synthesized setter and
    /// wants the same answer: there is no accessor body to hold the mark and no assignment inside the type
    /// to report. Reporting the auto-property and not this was the rule disagreeing with itself about which
    /// member kinds its own second shape is for.
    /// </remarks>
    [Test]
    [TestCase("public Rect Bounds;")]
    [TestCase("protected Rect Bounds;")]
    [TestCase("internal Rect Bounds;")]
    public void APublicFieldProcessReads_IsReported(string declaration)
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
            "a field anyone outside the node can assign changes what Process reads with no mark anywhere");
    }

    /// <remarks>
    /// The fix the diagnostic recommends, and the event counterpart of a setter that marks: accessors with
    /// bodies put the mark on the path every subscription takes, and the delegate field they write is then
    /// no different from any other marked state.
    /// </remarks>
    [Test]
    public void AnEventWithAccessorsThatMark_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Action? _invalidated;

                public event Action? Invalidated
                {
                    add
                    {
                        _invalidated += value;
                        MarkChanged();
                    }
                    remove
                    {
                        _invalidated -= value;
                        MarkChanged();
                    }
                }

                public override void Process(RenderNodeContext context)
                {
                    if (_invalidated is not null)
                        context.Publish(default);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A private event is reachable only from the node's own code, which is exactly what the assignment
    /// shape already reads - the subscription below is a write it finds and a mark it accepts. Reporting
    /// the declaration too would report the same state twice and reject this node.
    /// </remarks>
    [Test]
    public void APrivateEventProcessReads_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private event Action? Invalidated;

                public void Subscribe(Action handler)
                {
                    Invalidated += handler;
                    MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    if (Invalidated is not null)
                        context.Publish(default);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// readonly stops every assignment outside the declaring type's constructors, and a constructor runs
    /// before there is a recording to invalidate - the same reason an init accessor is not reported.
    /// </remarks>
    [Test]
    public void AReadonlyFieldProcessReads_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                public readonly Rect Bounds;

                public WellBehavedNode(Rect bounds) => Bounds = bounds;

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The read set is what makes a mutation matter, for an event as much as for a property: a subscriber
    /// list no Process reads can be rewritten from anywhere without a frame noticing.
    /// </remarks>
    [Test]
    public void AnEventProcessNeverReads_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class QuietNode : RenderNode
            {
                private Rect _drawn;

                public event Action? Invalidated;

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

    /// <remarks>
    /// A deconstruction is the assignment shape written once for several targets. Each element stands
    /// exactly where <c>_bounds = bounds</c> puts its name, so a node that drifts through it drifts on the
    /// same terms - and reading the two spellings differently is what let this mutator past the rule.
    /// </remarks>
    [Test]
    public void AMutatorDeconstructingIntoTrackedStateWithoutMarking_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;
                private float _opacity;

                public void Update(Rect bounds, float opacity)
                {
                    (_bounds, _opacity) = (bounds, opacity);
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                    context.Publish(new Rect(_opacity, 0f, 0f, 0f));
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "a deconstruction changes what Process reads exactly as two assignments would");
    }

    /// <remarks>
    /// The target of a nested deconstruction sits several tuples deep, so the walk out to the assignment
    /// has to keep going rather than stop at the innermost one.
    /// </remarks>
    [Test]
    public void AMutatorDeconstructingIntoTrackedStateThroughNestedTuples_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;
                private float _opacity;

                public void Update(Rect bounds, float opacity)
                {
                    (_bounds, (_opacity, _)) = (bounds, (opacity, 0f));
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                    context.Publish(new Rect(_opacity, 0f, 0f, 0f));
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "a nested tuple still names the node's own state on the left of an assignment");
    }

    /// <remarks>
    /// A deconstruction is judged by the same mark every other write is judged by, so a node that marks
    /// stays accepted and the fix the diagnostic recommends actually silences it.
    /// </remarks>
    [Test]
    public void AMutatorDeconstructingIntoTrackedStateAndMarkingTheNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;
                private float _opacity;

                public void Update(Rect bounds, float opacity)
                {
                    (_bounds, _opacity) = (bounds, opacity);
                    MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                    context.Publish(new Rect(_opacity, 0f, 0f, 0f));
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the mark covers a deconstruction as it covers a whole-field assignment");
    }

    /// <remarks>
    /// The shape without the hazard: a deconstruction whose targets are all locals leaves every value the
    /// recording was made from where it was, so there is nothing to mark.
    /// </remarks>
    [Test]
    public void AMutatorDeconstructingIntoLocalsOnly_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class QuietNode : RenderNode
            {
                private Rect _bounds;

                public void Inspect(Rect bounds, float opacity)
                {
                    (Rect candidate, float weight) = (bounds, opacity);
                    Use(candidate, weight);
                }

                private static void Use(Rect bounds, float opacity) { }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "writing locals changes nothing the recording was made from");
    }

    /// <remarks>
    /// The read side of the same syntax. A tuple built out of the node's state is a read however much it
    /// looks like the write, and only the left of the assignment tells them apart.
    /// </remarks>
    [Test]
    public void AMutatorReadingTrackedStateThroughATuple_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class QuietNode : RenderNode
            {
                private Rect _bounds;
                private float _opacity;

                public void Report()
                {
                    (Rect bounds, float opacity) = (_bounds, _opacity);
                    Use(bounds, opacity);
                }

                private static void Use(Rect bounds, float opacity) { }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                    context.Publish(new Rect(_opacity, 0f, 0f, 0f));
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "taking the state out into a tuple leaves the node's recording as valid as it was");
    }

    /// <remarks>
    /// A deconstruction reaches its targets through whatever receiver each element spells, so the receiver
    /// still decides whose state changed. Marking this node would say nothing about the other one.
    /// </remarks>
    [Test]
    public void AMutatorDeconstructingIntoAnotherNodesState_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class QuietNode : RenderNode
            {
                private Rect _bounds;
                private float _opacity;

                public void UpdateOther(QuietNode other, Rect bounds, float opacity)
                {
                    (other._bounds, other._opacity) = (bounds, opacity);
                    other.MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                    context.Publish(new Rect(_opacity, 0f, 0f, 0f));
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "another instance's state is not what this node's mark decides");
    }

    /// <remarks>
    /// What holds an execution callback that reads its own node to one answer. Such a callback is written
    /// inside Process, so the node state it reads is state Process reads, and an unmarked write to that
    /// state is reported on exactly the terms a value handed through call state is - which is the whole of
    /// why reading the node directly costs no guarantee that spelling the same read as call state keeps.
    /// </remarks>
    [Test]
    public void StateReadOnlyInsideAnExecutionCallback_IsStateProcessReads()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DrawingNode : RenderNode
            {
                private float _offset;

                public void SetOffset(float value) => _offset = value;

                public override void Process(RenderNodeContext context)
                    => context.PaintedSource(bounds => Consume(_offset));

                private static void Consume(float value) { }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "a node that only the drawing reads is still read by Process, so forgetting the mark is "
            + "reported rather than left to a replayed recording");
    }

    /// <remarks>
    /// <para>
    /// The guard rail against the fix proposed for the method-group false negative: making
    /// <c>MarkChanged</c> count only when it is the callee of an invocation. Marking is thread-confined
    /// today - see the comment on <c>RenderNode.MarkChanged</c> - so marshalling the mark onto the recording
    /// thread is a plausible way to write it, and that fix reports this correct node.
    /// </para>
    /// <para>
    /// A rule that wanted the real gap - a method group that is genuinely never invoked, pinned by
    /// <see cref="AMarkNamedButNeverInvoked_IsAKnownGapTheRuleMisses"/> - has to tell this shape apart from
    /// it rather than reject both.
    /// </para>
    /// </remarks>
    [Test]
    public void AMutatorHandingTheMarkToAScheduler_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Threading.Tasks;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ScheduledNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    Task.Run(MarkChanged);
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
            "handing the mark to a scheduler is still marking, and a rule that demanded an invocation "
            + "would reject it");
    }

    /// <remarks>
    /// The same guard rail one step closer to the gap: the method group is stored in a delegate and then
    /// invoked through it, so the mark does run, but the name <c>MarkChanged</c> never appears as the callee
    /// of an invocation. Requiring one reports this node too.
    /// </remarks>
    [Test]
    public void AMutatorInvokingTheMarkThroughADelegate_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DeferredNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    Action mark = MarkChanged;
                    mark();
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
            "the mark runs, so the node is correct, however the invocation is spelled");
    }

    /// <remarks>
    /// <para>
    /// The guard rail against the fix proposed for the abstract-base false negative: scanning inherited
    /// members for unmarked assignments. This is the shape <c>BrushRenderNode</c> and the six nodes deriving
    /// from it are written in - a protected helper on the base assigns and reports whether anything changed,
    /// and the derived override marks once for the whole update - and that fix reports every one of them.
    /// </para>
    /// <para>
    /// Excusing the base helper needs the rule to look at its callers, not at what it can itself reach, which
    /// is a wider excuse on a public extension point than the gap it closes. The gap this leaves is pinned by
    /// <see cref="AnUnmarkedMutatorOnABaseType_IsAKnownGapTheRuleMisses"/>; the two differ only in whether the
    /// derived caller marks, which is the line any such fix has to draw.
    /// </para>
    /// </remarks>
    [Test]
    public void ABaseHelperWhoseDerivedCallerMarks_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal abstract class ShapeRenderNode : RenderNode
            {
                protected ShapeRenderNode(Rect bounds) => Bounds = bounds;

                protected Rect Bounds { get; private set; }

                protected bool Update(Rect bounds)
                {
                    if (Bounds == bounds)
                        return false;

                    Bounds = bounds;
                    return true;
                }
            }

            internal sealed class BoxRenderNode : ShapeRenderNode
            {
                private float _radius;

                public BoxRenderNode(Rect bounds, float radius)
                    : base(bounds)
                {
                    _radius = radius;
                }

                public bool Update(Rect bounds, float radius)
                {
                    bool changed = Update(bounds);
                    if (_radius != radius)
                    {
                        _radius = radius;
                        changed = true;
                    }

                    if (changed)
                        MarkChanged();

                    return changed;
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                    context.Publish(new Rect(_radius, 0, 0, 0));
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the derived update marks for the whole change, so neither half of it is a node going stale");
    }

    /// <remarks>
    /// <para>
    /// A known gap, recorded so it is visible rather than endorsed. The base helper assigns state the derived
    /// node's <c>Process</c> reads and nobody marks, so this node does go stale - the rule is silent because
    /// the member scan reads only <c>type.GetMembers()</c>, and the analysis stops at the base because a type
    /// with no <c>Process</c> of its own reads no state at all.
    /// </para>
    /// <para>
    /// Closing it was measured and declined: the obvious fix reports the correct shape pinned by
    /// <see cref="ABaseHelperWhoseDerivedCallerMarks_IsNotReported"/> twelve times over in this repository
    /// alone. A later fix that tells the two apart <em>should</em> make this case report; flipping this test
    /// to <c>Does.Contain("BESG005")</c> is then the right response, and is the point of writing it down.
    /// </para>
    /// </remarks>
    [Test]
    public void AnUnmarkedMutatorOnABaseType_IsAKnownGapTheRuleMisses()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal abstract class ShapeRenderNode : RenderNode
            {
                protected Rect Bounds { get; private set; }

                protected void UpdateBounds(Rect bounds) => Bounds = bounds;
            }

            internal sealed class BoxRenderNode : ShapeRenderNode
            {
                public void Update(Rect bounds) => UpdateBounds(bounds);

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "this node really does go stale; the silence is a recorded limit of the rule, not a verdict "
            + "that the node is correct");
    }

    /// <remarks>
    /// <para>
    /// The other known gap, recorded on the same terms. Naming <c>MarkChanged</c> clears the member whether
    /// or not the delegate is ever invoked, so this node goes stale and the rule says nothing.
    /// </para>
    /// <para>
    /// Closing it by demanding an invocation was measured and declined: it reports the correct shapes pinned
    /// by <see cref="AMutatorHandingTheMarkToAScheduler_IsNotReported"/> and
    /// <see cref="AMutatorInvokingTheMarkThroughADelegate_IsNotReported"/>. A later fix that tells a
    /// discarded method group from a marshalled one <em>should</em> make this case report.
    /// </para>
    /// </remarks>
    [Test]
    public void AMarkNamedButNeverInvoked_IsAKnownGapTheRuleMisses()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ForgetfulNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    Action mark = MarkChanged;
                    Discard(mark);
                }

                private static void Discard(Action callback) { }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the mark never runs, so this node really does go stale; the silence is a recorded limit of "
            + "the rule, not a verdict that the node is correct");
    }

    /// <remarks>
    /// A ref local is the assignment shape with the name moved one statement up: <c>alias = value</c>
    /// changes the very storage <c>Process</c> reads, and only the alias standing between the field and the
    /// <c>=</c> differs. Reading the two out differently let an ordinary mutator past the rule, because the
    /// field's own reference sits under a <c>ref</c> that writes nothing and the write names a local this
    /// rule never tracked.
    /// </remarks>
    [Test]
    public void AMutatorWritingThroughARefLocalAlias_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    ref Rect alias = ref _bounds;
                    alias = bounds;
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
            "the alias writes the field Process reads, and no mark says the recording went stale");
    }

    [Test]
    public void AMutatorWritingThroughARefVarAlias_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    ref var alias = ref _bounds;
                    alias = bounds;
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
            "ref var names the same storage ref Rect does");
    }

    [Test]
    public void AMutatorThatMarksAfterWritingThroughARefLocalAlias_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    ref Rect alias = ref _bounds;
                    alias = bounds;
                    MarkChanged();
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
            "the mark is what the rule asks for, however the write is spelled");
    }

    /// <remarks>
    /// The bound that keeps the alias tracking from becoming a rule about every <c>ref</c>: what a ref local
    /// aliases decides the answer, and a local, or an element of one, is not state <c>Process</c> reads.
    /// </remarks>
    [Test]
    public void AMutatorWritingThroughARefLocalAliasOfALocal_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;

                public Rect Measure(Rect bounds)
                {
                    Rect scratch = default;
                    ref Rect alias = ref scratch;
                    alias = bounds;
                    return scratch;
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
            "the alias names a local, which no recording ever read");
    }

    [Test]
    public void AMutatorWritingThroughARefLocalAliasOfALocalArrayElement_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;

                public Rect Measure(Rect bounds)
                {
                    Rect[] scratch = new Rect[1];
                    ref Rect alias = ref scratch[0];
                    alias = bounds;
                    return scratch[0];
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
            "an element of a local is a local, however the alias reaches it");
    }

    /// <remarks>
    /// Taking a writable reference is not itself the change: a member read through the alias leaves the
    /// recording as it was, and reporting it would make the rule one about <c>ref</c> rather than about
    /// mutation.
    /// </remarks>
    [Test]
    public void AMemberOnlyReadingThroughARefLocalAlias_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private Rect _bounds;

                public float Measure()
                {
                    ref Rect alias = ref _bounds;
                    return alias.X;
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
            "nothing was written, so there is no later frame to invalidate");
    }

    [Test]
    public void AMutatorWritingThroughAnAliasOfAnAlias_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    ref Rect first = ref _bounds;
                    ref Rect second = ref first;
                    second = bounds;
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
            "a second name for the first alias reaches the same storage, so a chain of them is one write");
    }

    /// <remarks>
    /// The bound this tracking is written to: reading a body in source order says which storage an alias
    /// was given, not which one it holds at a given statement, so a rebound alias is dropped rather than
    /// guessed at. The silence costs a report and never invents one.
    /// </remarks>
    [Test]
    public void AMutatorWritingThroughARebindableAlias_IsAKnownGapTheRuleMisses()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ForgetfulNode : RenderNode
            {
                private Rect _bounds;
                private Rect _scratch;

                public void Update(Rect bounds, bool other)
                {
                    ref Rect alias = ref _bounds;
                    if (other)
                        alias = ref _scratch;

                    alias = bounds;
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
            "which storage the alias holds at the write is a question about paths this rule does not read; "
            + "the silence is a recorded limit, not a verdict that the node is correct");
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
