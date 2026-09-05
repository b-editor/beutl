using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGeneratorTest;

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

    [Test]
    public void APrivateHelperWhoseOnlyCallerMarks_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                private bool SetBounds(Rect bounds)
                {
                    if (_bounds == bounds)
                        return false;

                    _bounds = bounds;
                    return true;
                }

                public bool Update(Rect bounds)
                {
                    bool changed = SetBounds(bounds);
                    if (changed)
                        MarkChanged();

                    return changed;
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
            "nothing outside the node can reach the write except through an update that marks, so there "
            + "is no way to leave this node holding a recording of the old bounds");
    }

    [Test]
    public void APublicMutatorAMarkingMemberAlsoCalls_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;
                private float _opacity;

                public void UpdateBounds(Rect bounds)
                {
                    _bounds = bounds;
                }

                public void UpdateAll(Rect bounds, float opacity)
                {
                    UpdateBounds(bounds);
                    _opacity = opacity;
                    MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                    context.Publish(new Rect(_opacity, 0, 0, 0));
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "a public mutator is its own way in, so a marking caller elsewhere says nothing about the "
            + "holder that calls this one on its own");
    }

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

    [Test]
    public void AnUnmarkedMutatorOnABaseType_IsReported()
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
            Does.Contain("BESG005"),
            "the base declares the mutator and the derived node's Process reads what it writes, so this "
            + "node goes stale for a write no member of its own type list carries");
    }

    [Test]
    public void AMutatorOnABaseTypeThatMarksTheNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal abstract class ShapeRenderNode : RenderNode
            {
                protected Rect Bounds { get; private set; }

                protected void UpdateBounds(Rect bounds)
                {
                    Bounds = bounds;
                    MarkChanged();
                }
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
            "the mark is what the rule asks for, wherever in the chain the write is declared");
    }

    [Test]
    public void AMutatorOnABaseTypeWritingStateProcessDoesNotRead_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal abstract class ShapeRenderNode : RenderNode
            {
                private Rect _measured;

                protected Rect Bounds { get; private set; }

                public Rect Measured => _measured;

                protected void Measure(Rect bounds) => _measured = bounds;
            }

            internal sealed class BoxRenderNode : ShapeRenderNode
            {
                public void Update(Rect bounds) => Measure(bounds);

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "no recording was built from the field the base writes, so nothing about it can go stale");
    }

    [Test]
    public void AnUnmarkedMutatorOnABaseTypeWithItsOwnProcess_IsReportedOnce()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal class ShapeRenderNode : RenderNode
            {
                protected Rect Bounds { get; private set; }

                protected void UpdateBounds(Rect bounds) => Bounds = bounds;

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(Bounds);
                }
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
            diagnostics.Where(static d => d.Id == "BESG005"),
            Has.Exactly(1).Items,
            "the base reads the same state its own Process reads, so its analysis already reports the "
            + "write; saying it again once per derived node would report one line as many times as the "
            + "type is inherited");
    }

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

    [Test]
    public void AMutatorAddingToACollectionProcessReads_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Collections.Generic;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private readonly List<Rect> _items = new();

                public void Update(Rect bounds)
                {
                    _items.Add(bounds);
                }

                public override void Process(RenderNodeContext context)
                {
                    foreach (Rect item in _items)
                        context.Publish(item);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "the list Process reads holds something it did not before, so the recording built from the "
            + "old contents is as stale as one built from an overwritten field");
    }

    [Test]
    public void AMutatorAddingThroughAConditionalAccess_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Collections.Generic;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private List<Rect>? _items;

                public void Update(Rect bounds)
                {
                    _items?.Add(bounds);
                }

                public override void Process(RenderNodeContext context)
                {
                    if (_items is null)
                        return;

                    foreach (Rect item in _items)
                        context.Publish(item);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "a conditional access spells the receiver once, in front of the chain, and the call it guards "
            + "is the same call on the same state");
    }

    [Test]
    public void AMutatorMarkingAfterAddingToACollection_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Collections.Generic;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private readonly List<Rect> _items = new();

                public void Update(Rect bounds)
                {
                    _items.Add(bounds);
                    MarkChanged();
                }

                public override void Process(RenderNodeContext context)
                {
                    foreach (Rect item in _items)
                        context.Publish(item);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the mark is what the rule asks for, however the contents changed");
    }

    [Test]
    public void AMemberOnlyReadingACollection_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Collections.Generic;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private readonly List<Rect> _items = new();

                public bool Holds(Rect bounds) => _items.Contains(bounds);

                public int Find(Rect bounds) => _items.IndexOf(bounds);

                public int Count => _items.Count;

                public override void Process(RenderNodeContext context)
                {
                    foreach (Rect item in _items)
                        context.Publish(item);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "Contains, IndexOf and Count answer a question about the list and leave it as it was, so a "
            + "rule that asked for a mark here would ask it of every read");
    }

    [Test]
    public void AMutatorAddingToACollectionProcessDoesNotRead_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Collections.Generic;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private readonly List<Rect> _measured = new();
                private Rect _bounds;

                public void Measure(Rect bounds) => _measured.Add(bounds);

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "no recording was built from that list, so nothing about it can go stale");
    }

    [Test]
    public void AMemberCallingAMutatorNameOnAnImmutableValue_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class WellBehavedNode : RenderNode
            {
                private string _label = string.Empty;
                private TimeSpan _offset;

                public string Sanitized() => _label.Replace(" ", string.Empty);

                public TimeSpan Delayed(TimeSpan delay) => _offset.Add(delay);

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(new Rect(_label.Length, (float)_offset.TotalSeconds, 0, 0));
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "a string and a readonly struct answer with a new value and leave the state holding what it "
            + "held, so the mutator name on one of them writes nothing");
    }

    [Test]
    public void AMutatorMarkingOnlyInAnUninvokedLocalFunction_IsReported()
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

                    void Invalidate() => MarkChanged();
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
            "nothing names the local function, so the mark inside it is not in the program the author "
            + "ships and the node renders stale content");
    }

    [Test]
    public void AMutatorMarkingOnlyInAnUninvokedLambda_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    Action invalidate = () => MarkChanged();
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
            "the delegate is made and never invoked, so the mark inside it never runs");
    }

    [Test]
    public void AMutatorInvokingTheLocalFunctionThatMarks_IsNotReported()
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

                    void Invalidate() => MarkChanged();
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
            "the local function runs, so the mark runs with it");
    }

    [Test]
    public void AMutatorHandingAMarkingLambdaToAScheduler_IsNotReported()
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
                    Task.Run(() => MarkChanged());
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
            "the lambda is handed to a scheduler, which is still marking");
    }

    [Test]
    public void AMutatorMarkingOnlyInAConditionalHelper_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Diagnostics;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    Invalidate();
                }

                [Conditional("DEBUG")]
                private void Invalidate() => MarkChanged();

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG005"),
            "without DEBUG the compiler removes the call, so the shipped mutator never marks");
    }

    [Test]
    public void AMutatorMarkingInAConditionalHelper_IsNotReportedWhenTheSymbolIsDefined()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze(
            """
            using System.Diagnostics;
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

                [Conditional("DEBUG")]
                private void Invalidate() => MarkChanged();

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """,
            CSharpParseOptions.Default.WithPreprocessorSymbols("DEBUG"));

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "DEBUG is defined, so the call to the helper is compiled and the mark runs");
    }

    [Test]
    public void AMutatorMarkingInAMultiplyConditionalHelper_IsNotReportedWhenOneSymbolIsDefined()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze(
            """
            using System.Diagnostics;
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

                [Conditional("DEBUG")]
                [Conditional("TRACE")]
                private void Invalidate() => MarkChanged();

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """,
            CSharpParseOptions.Default.WithPreprocessorSymbols("TRACE"));

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "TRACE is defined, so the call is compiled however many other symbols are named");
    }

    [Test]
    public void AMutatorWhoseMarkingLocalFunctionIsNamedOnlyFromAnUninvokedLambda_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;

                    Action unused = () => Invalidate();

                    void Invalidate() => MarkChanged();
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
            "nothing that runs names the local function, so the mark inside it is not in the shipped "
            + "program however plainly the source spells it");
    }

    [Test]
    public void AMutatorMarkingOnlyInADiscardedLambda_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    _bounds = bounds;
                    _ = (Action)(() => MarkChanged());
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
            "the delegate is discarded where it is made, so the mark inside it can never run");
    }

    [Test]
    public void AMutatorWhoseAssignmentAndMarkBothSitInAnUninvokedLocalFunction_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class QuietNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    void Apply()
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
            "nothing runs, so there is no recording for this member to leave stale");
    }

    [Test]
    public void AMutatorAssigningFromAnInvokedLocalFunction_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class DriftingNode : RenderNode
            {
                private Rect _bounds;

                public void Update(Rect bounds)
                {
                    Apply();

                    void Apply() => _bounds = bounds;
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
            "the local function runs and nothing marks, so the node renders stale content");
    }

    private static ImmutableArray<Diagnostic> Analyze(string source)
        => Analyze(source, CSharpParseOptions.Default);

    /// <summary>Runs the analyzer over <paramref name="source"/> parsed with <paramref name="parseOptions"/>.</summary>
    /// <remarks>
    /// The parse options are a parameter for the sake of the preprocessor symbols: a <c>[Conditional]</c>
    /// call is kept or dropped by the symbols defined where it is written, so a harness that could not vary
    /// them could not tell a mark the build keeps from one it removes.
    /// </remarks>
    private static ImmutableArray<Diagnostic> Analyze(string source, CSharpParseOptions parseOptions)
    {
        CSharpCompilation compilation = CreateCompilation(source, parseOptions);

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
        => CreateCompilation(source, CSharpParseOptions.Default);

    private static CSharpCompilation CreateCompilation(string source, CSharpParseOptions parseOptions)
        => CSharpCompilation.Create(
            "AnalyzerTest",
            [
                CSharpSyntaxTree.ParseText(RenderNodeStubs, parseOptions),
                CSharpSyntaxTree.ParseText(source, parseOptions),
            ],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
                .ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
