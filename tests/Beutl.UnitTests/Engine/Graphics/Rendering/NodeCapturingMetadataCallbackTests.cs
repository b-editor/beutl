using System.Collections;
using System.Runtime.CompilerServices;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class NodeCapturingMetadataCallbackTests
{
    private static readonly Rect s_domain = new(0, 0, 200, 100);
    private static readonly Rect s_sourceBounds = new(0, 0, 40, 20);

    [Test]
    public void ANodeReadingItsOwnPropertyFromABoundsMapping_RecordsAndRasterizes()
    {
        using var node = new ShiftedSourceNode(12);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(
                measurement.OutputBounds,
                Is.EqualTo(s_sourceBounds.Translate(new Vector(12, 0))),
                "the mapping must have answered from the node's own property");
            Assert.That(rasterization.Bitmap, Is.Not.Null);
        });
    }

    [Test]
    public void TwoNodesOfOneTypeReadingDifferentValues_CompileOneStructuralPlan()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new ShiftedSourceNode(12));
        using RenderNodeRenderer renderer = CreateRenderer(root);

        renderer.Rasterize().Dispose();
        long afterFirstNode = renderer.StructuralPlanCacheStatistics.Compilations;
        root.SetChild(0, new ShiftedSourceNode(31));
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(afterFirstNode, Is.EqualTo(1));
            Assert.That(
                renderer.StructuralPlanCacheStatistics.Compilations,
                Is.EqualTo(1),
                "what a metadata callback reads is request data; a second node of the same type must re-run "
                + "the compiled plan rather than compile a second one");
            Assert.That(renderer.StructuralPlanCacheStatistics.Hits, Is.GreaterThan(0));
            Assert.That(
                renderer.Measure().OutputBounds,
                Is.EqualTo(s_sourceBounds.Translate(new Vector(31, 0))),
                "the shared plan must still be re-run over the second node's own value");
        });
    }

    [Test]
    public void ACallbackBoundToTheDeclaringNode_IsAccepted()
    {
        using var node = new ShiftedSourceNode(3);

        Assert.DoesNotThrow(() => node.CreateBounds());
    }

    [Test]
    public void StructuralIdentity_SeparatesDeclarationsAndGenericInstantiations()
    {
        object shiftByFour = RenderBoundsContract.Create(ShiftRight, ShiftLeft).StructuralIdentity;
        object shiftBySeven = RenderBoundsContract.Create(ShiftRight, ShiftLeft).StructuralIdentity;
        object otherDeclaration = RenderBoundsContract.Create(ShiftLeft, ShiftRight).StructuralIdentity;
        object overInt = Passthrough(0).StructuralIdentity;
        object overLong = Passthrough(0L).StructuralIdentity;

        Assert.Multiple(() =>
        {
            Assert.That(shiftBySeven, Is.EqualTo(shiftByFour));
            Assert.That(otherDeclaration, Is.Not.EqualTo(shiftByFour));
            Assert.That(overLong, Is.Not.EqualTo(overInt),
                "two instantiations of one generic callback are two declarations");
        });
    }

    private static Rect ShiftRight(Rect value) => value.Translate(new Vector(1, 0));

    private static Rect ShiftLeft(Rect value) => value.Translate(new Vector(-1, 0));

    private static RenderBoundsContract Passthrough<TState>(TState state)
        => RenderBoundsContract.Create(
            state,
            static (_, input) => input,
            static (_, requested) => requested);

    [TestCaseSource(nameof(RejectedIdentities))]
    public void AnIdentityThatIsNotANode_IsStillRejected(object key)
    {
        ArgumentException? failure = Assert.Throws<ArgumentException>(
            () => RenderIdentityKeyValidator.ThrowIfInvalid(key, "callback"));

        Assert.That(failure!.Message, Does.Contain("must be a lightweight, immutable CPU value"));
    }

    private static IEnumerable<TestCaseData> RejectedIdentities()
    {
        // Every arm of the validator's list, so admitting the node cannot have widened another by sharing a
        // clause with it. An uninitialized instance is enough for the arms whose type is only pattern-tested;
        // a session or a writer has no constructor a test can reach.
        yield return Uninitialized<MemoryStream>();
        yield return Uninitialized<RenderResource<object>>();
        yield return Uninitialized<RenderNodeContext>();
        yield return Uninitialized<RenderRequest>();
        yield return Uninitialized<RenderRequestOptions>();
        yield return Uninitialized<RecordedRenderGraph>();
        yield return Uninitialized<RecordedRenderGraphBuilder>();
        yield return Uninitialized<RenderResourceSlot<object>>();
        yield return Uninitialized<RenderFragmentHandle>();
        yield return Uninitialized<RenderExecutionInput>();
        yield return Uninitialized<RenderCallbackCanvas>();
        yield return Uninitialized<OpaqueRenderSession>();
        yield return Uninitialized<OpaqueRenderOutput>();
        yield return Uninitialized<GeometrySession>();
        yield return Uninitialized<ShaderExecutionContext>();
        yield return Uninitialized<ShaderUniformWriter>();
        yield return Uninitialized<ShaderResourceWriter>();
        yield return Uninitialized<TargetScopeSession>();
        yield return Uninitialized<TargetCommandSession>();
        yield return Uninitialized<RawTargetScopeSession>();
        yield return Uninitialized<RawTargetCommandSession>();
        yield return new TestCaseData(new int[1]).SetArgDisplayNames("Array");
        yield return new TestCaseData(new List<int>()).SetArgDisplayNames("IList");
        yield return new TestCaseData(new Dictionary<int, int>()).SetArgDisplayNames("IDictionary");
        yield return new TestCaseData(new Queue<int>()).SetArgDisplayNames("ICollection");
    }

    private static TestCaseData Uninitialized<T>()
        => new TestCaseData(RuntimeHelpers.GetUninitializedObject(typeof(T)))
            .SetArgDisplayNames(typeof(T).Name);

    [Test]
    public void ANodeThatIsAlsoAMutableCollection_IsStillRejected()
    {
        using var node = new CollectionNode();

        Assert.Throws<ArgumentException>(
            () => RenderIdentityKeyValidator.ThrowIfInvalid(node, "callback"));
    }

    [Test]
    public void ACallbackReachingAResourceThroughItsNode_IsNotWhatTheValidatorReads()
    {
        using var node = new ResourceHoldingNode();
        object resource = RuntimeHelpers.GetUninitializedObject(typeof(RenderResource<object>));
        Func<int> boundToResource = resource.GetHashCode;
        Func<Rect, Rect> boundToNode = node.CreateResourceReadingCallback();

        Assert.Multiple(() =>
        {
            Assert.That(boundToNode.Target, Is.SameAs(node));
            Assert.DoesNotThrow(
                () => RenderDescriptionValidation.ValidatePureMetadataCallback(boundToNode, "callback"),
                "the target is the node, and no clause reads a node's fields");
            Assert.That(boundToResource.Target, Is.SameAs(resource));
            Assert.Throws<ArgumentException>(
                () => RenderDescriptionValidation.ValidatePureMetadataCallback(boundToResource, "callback"));
        });
    }

    [Test]
    public void ALambdaClosingOverALocal_IsNotWhatTheValidatorReads()
    {
        var offset = new Vector(5, 0);
        Func<Rect, Rect> closesOverALocal = r => r.Translate(offset);

        Assert.Multiple(() =>
        {
            Assert.That(closesOverALocal.Target, Is.Not.Null);
            Assert.That(
                closesOverALocal.Target!.GetType().Name,
                Does.StartWith("<>c__DisplayClass"),
                "a closure over anything besides this arrives as a compiler display class");
            Assert.DoesNotThrow(
                () => RenderDescriptionValidation.ValidatePureMetadataCallback(closesOverALocal, "callback"),
                "no clause on the list answers for a display class, so the runtime is not what stops this");
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode root)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_domain,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    /// <summary>A source shifted by a distance the node holds, read by the node's own bounds mapping.</summary>
    private sealed class ShiftedSourceNode(float offset) : RenderNode
    {
        public float Offset { get; } = offset;

        public RenderBoundsContract CreateBounds()
            => RenderBoundsContract.Create(
                r => r.Translate(new Vector(Offset, 0)),
                r => r.Translate(new Vector(-Offset, 0)));

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.CreateRequestLocal(
                static session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_sourceBounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale));

            context.Publish(context.OpaqueMap(source, OpaqueRenderDescription.CreateRequestLocal(
                static session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(session.Inputs[0].Draw);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Map(CreateBounds()),
                RenderHitTestContract.AnyInput,
                RenderValueCardinality.Single,
                RenderScaleContract.PreserveInputSupply)));
        }
    }

    private sealed class ResourceHoldingNode : RenderNode
    {
        private readonly RenderResource<object> _held =
            (RenderResource<object>)RuntimeHelpers.GetUninitializedObject(typeof(RenderResource<object>));

        public Func<Rect, Rect> CreateResourceReadingCallback()
            => r => _held is null ? Rect.Empty : r;

        public override void Process(RenderNodeContext context) => context.PassThrough();
    }

    private sealed class CollectionNode : RenderNode, ICollection
    {
        public int Count => 0;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public void CopyTo(Array array, int index)
        {
        }

        public IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();

        public override void Process(RenderNodeContext context) => context.PassThrough();
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
