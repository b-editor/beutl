using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

/// <summary>
/// Pins the planner traits the in-tree render nodes declare and the cache decisions they produce, so a
/// reversed mapping is caught instead of silently changing which fragments survive a remapping scope.
/// </summary>
[TestFixture]
public sealed class InTreeDeclaredTraitTests
{
    private static readonly Rect s_domain = new(0, 0, 256, 128);

    private static readonly Matrix s_subpixelShift = Matrix.CreateTranslation(3.25f, 4.5f);

    [TestCase(TransformOperator.Prepend, false, true, RenderDeviceGridMapping.Remapped)]
    [TestCase(TransformOperator.Prepend, true, true, RenderDeviceGridMapping.Preserved)]
    [TestCase(TransformOperator.Append, false, false, RenderDeviceGridMapping.Remapped)]
    [TestCase(TransformOperator.Append, true, false, RenderDeviceGridMapping.Preserved)]
    [TestCase(TransformOperator.Set, false, false, RenderDeviceGridMapping.Remapped)]
    [TestCase(TransformOperator.Set, true, false, RenderDeviceGridMapping.Remapped)]
    public void TransformRenderNode_DeclaresEligibilityAndGridMappingIndependently(
        TransformOperator transformOperator,
        bool identityMatrix,
        bool expectedValueReplayMap,
        RenderDeviceGridMapping expectedMapping)
    {
        using var transform = new TransformRenderNode(
            identityMatrix ? Matrix.Identity : s_subpixelShift,
            transformOperator);
        transform.AddChild(NewRectangleNode());

        TargetScopeDescription description = RecordSingleScope(transform);

        Assert.Multiple(() =>
        {
            Assert.That(description.IsValueReplayMap, Is.EqualTo(expectedValueReplayMap));
            Assert.That(description.DeviceGridMapping, Is.EqualTo(expectedMapping));
        });
    }

    [TestCase(false, RenderDeviceGridMapping.Remapped)]
    [TestCase(true, RenderDeviceGridMapping.Preserved)]
    public void DrawableGroupTransform_DeclaresItsGridMappingFromTheResolvedMatrix(
        bool identityMatrix,
        RenderDeviceGridMapping expectedMapping)
    {
        using DrawableGroup.CustomTransformRenderNode transform = NewGroupTransformNode(identityMatrix);
        transform.AddChild(NewRectangleNode());

        TargetScopeDescription description = RecordSingleScope(transform);

        Assert.Multiple(() =>
        {
            Assert.That(description.IsValueReplayMap, Is.True);
            Assert.That(description.DeviceGridMapping, Is.EqualTo(expectedMapping));
        });
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void DrawableGroupTransform_BypassesTheTextCacheOnlyWhenItRemapsTheGrid(
        bool identityMatrix,
        bool expectBypass)
    {
        using DrawableGroup.CustomTransformRenderNode transform = NewGroupTransformNode(identityMatrix);
        RenderNode text = NewTextNode();
        text.Cache.ReportRenderCount(RenderNodeCache.Count);
        transform.AddChild(text);

        Assert.That(ResolveSingleCacheDecision(transform).BypassReason, Is.EqualTo(
            expectBypass ? RenderCacheBypassReason.DeviceGridDependentOutput : RenderCacheBypassReason.None));
    }

    [Test]
    public void VectorSourcesConservativelyDeclareDeviceGridPhaseDependence()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                DeclaredSensitivity(NewTextNode()),
                Is.EqualTo(RenderDeviceGridSensitivity.PhaseDependent));
            Assert.That(
                DeclaredSensitivity(NewRectangleNode()),
                Is.EqualTo(RenderDeviceGridSensitivity.PhaseDependent));
            Assert.That(
                DeclaredSensitivity(new EllipseRenderNode(
                    new Rect(0, 0, 40, 30),
                    Brushes.Resource.White,
                    null)),
                Is.EqualTo(RenderDeviceGridSensitivity.PhaseDependent));
        });
    }

    [TestCase(true, TransformOperator.Prepend, false, true)]
    [TestCase(true, TransformOperator.Prepend, true, false)]
    [TestCase(true, TransformOperator.Append, false, true)]
    [TestCase(true, TransformOperator.Append, true, false)]
    [TestCase(false, TransformOperator.Prepend, false, true)]
    public void TransformOverASource_BypassesPhaseDependentContentUnderARemappingScope(
        bool useText,
        TransformOperator transformOperator,
        bool identityMatrix,
        bool expectBypass)
    {
        RenderNode source = useText ? NewTextNode() : NewRectangleNode();
        source.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var transform = new TransformRenderNode(
            identityMatrix ? Matrix.Identity : s_subpixelShift,
            transformOperator);
        transform.AddChild(source);

        Assert.That(ResolveSingleCacheDecision(transform).BypassReason, Is.EqualTo(
            expectBypass ? RenderCacheBypassReason.DeviceGridDependentOutput : RenderCacheBypassReason.None));
    }

    [Test]
    public void TheGridPreservingInTreeScopesDeclareThatTheyPreserveTheGrid()
    {
        using var push = new PushRenderNode();
        push.AddChild(NewRectangleNode());
        using var rectClip = new RectClipRenderNode(s_domain, ClipOperation.Intersect);
        rectClip.AddChild(NewRectangleNode());
        using var geometryClip = new GeometryClipRenderNode(
            new RectGeometry
            {
                Width = { CurrentValue = s_domain.Width },
                Height = { CurrentValue = s_domain.Height },
            }.ToResource(CompositionContext.Default),
            ClipOperation.Intersect);
        geometryClip.AddChild(NewRectangleNode());

        Assert.Multiple(() =>
        {
            Assert.That(
                RecordSingleScope(push).DeviceGridMapping,
                Is.EqualTo(RenderDeviceGridMapping.Preserved));
            Assert.That(
                RecordSingleScope(rectClip).DeviceGridMapping,
                Is.EqualTo(RenderDeviceGridMapping.Preserved));
            Assert.That(
                RecordSingleScope(geometryClip).DeviceGridMapping,
                Is.EqualTo(RenderDeviceGridMapping.Preserved));
        });
    }

    private static TargetScopeDescription RecordSingleScope(RenderNode node)
    {
        using RenderRequest request = CreateRequest(cacheEnabled: false);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        return ((TargetScopeRenderFragmentPayload)GetSingleRoot(graph).Payload!).Description;
    }

    private static RenderCacheDecision ResolveSingleCacheDecision(RenderNode node)
    {
        using RenderRequest request = CreateRequest(cacheEnabled: true, RenderRequestPurpose.Frame);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        using CompiledRenderRequest compiled = new RenderRequestCompiler(
            renderCacheContext: new RenderCacheResolutionContext(
                RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
                new RenderCacheDeviceContextIdentity("device", "context")))
            .Compile(request, graph);
        return compiled.CacheResolution.Decisions.Single();
    }

    private static RenderDeviceGridSensitivity DeclaredSensitivity(RenderNode node)
    {
        using (node)
        {
            using RenderRequest request = CreateRequest(cacheEnabled: false);
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            var payload = (OpaqueRenderFragmentPayload)GetSingleRoot(graph).Payload!;
            return payload.Description.DeviceGridSensitivity;
        }
    }

    private static DrawableGroup.CustomTransformRenderNode NewGroupTransformNode(bool identityMatrix)
        => new(
            identityMatrix
                ? null
                : new TranslateTransform(s_subpixelShift.M31, s_subpixelShift.M32)
                    .ToResource(CompositionContext.Default),
            default,
            s_domain.Size,
            AlignmentX.Left,
            AlignmentY.Top,
            new MemoryNode<Rect>(s_domain));

    private static RectangleRenderNode NewRectangleNode()
        => new(new Rect(0, 0, 40, 30), Brushes.Resource.White, null);

    private static TextRenderNode NewTextNode()
    {
        var text = new FormattedText
        {
            Font = TypefaceProvider.Typeface().FontFamily,
            Size = 48f,
            Text = "ab",
        };
        return new TextRenderNode(text, Brushes.Resource.White, null);
    }

    private static RenderRequest CreateRequest(
        bool cacheEnabled,
        RenderRequestPurpose purpose = RenderRequestPurpose.Auxiliary)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            purpose,
            targetDomain: s_domain,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: cacheEnabled ? RenderCacheOptions.Enabled : RenderCacheOptions.Disabled));

    private static RenderFragmentReference GetSingleRoot(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
    }
}
