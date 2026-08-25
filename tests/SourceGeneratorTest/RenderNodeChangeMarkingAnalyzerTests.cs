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
                public bool HasChanges { get; set; }

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
                    HasChanges = true;
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

                private void Invalidate() => HasChanges = true;

                public override void Process(RenderNodeContext context)
                {
                    context.Publish(_bounds);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
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
                        HasChanges = true;
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

    private static ImmutableArray<Diagnostic> Analyze(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
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
}
