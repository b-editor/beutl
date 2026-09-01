using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class RegionAnalysisReuseTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 24);

    [Test]
    public void Compile_RunsOneRegionAnalysisForASingleRequest()
    {
        using var node = new RectangleRenderNode(s_bounds, Brushes.Resource.White, null);
        using var request = new RenderRequest(Options());
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var compiler = new RenderRequestCompiler();

        using CompiledRenderRequest compiled = compiler.Compile(request, graph);

        Assert.Multiple(() =>
        {
            Assert.That(compiler.RegionAnalysisCount, Is.EqualTo(1));
            Assert.That(compiled.Regions.FinalCommitBounds, Is.EqualTo(s_bounds));
            Assert.That(
                compiled.Regions.FragmentRequirements,
                Is.Not.Empty,
                "the surviving analysis must be the full one, not the measurement-only pass");
        });
    }

    [Test]
    public void ResolveMetadata_RunsNoFullRegionAnalysis()
    {
        using var node = new RectangleRenderNode(s_bounds, Brushes.Resource.White, null);
        using var request = new RenderRequest(Options(RenderRequestPurpose.Bounds));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var compiler = new RenderRequestCompiler();

        RenderNodeMeasurement measurement = compiler.ResolveMetadata(request, graph);

        Assert.Multiple(() =>
        {
            Assert.That(compiler.RegionAnalysisCount, Is.Zero);
            Assert.That(measurement.OutputBounds, Is.EqualTo(s_bounds));
        });
    }

    [Test]
    public void CompileAfterMetadata_RunsOneRegionAnalysisForTheRequestItResumes()
    {
        using var node = new RectangleRenderNode(s_bounds, Brushes.Resource.White, null);
        using var request = new RenderRequest(Options());
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var compiler = new RenderRequestCompiler();
        RenderNodeMeasurement measurement = compiler.ResolveMetadata(request, graph);

        using CompiledRenderRequest compiled = compiler.CompileAfterMetadata(request, graph, measurement);

        Assert.Multiple(() =>
        {
            Assert.That(compiler.RegionAnalysisCount, Is.EqualTo(1));
            Assert.That(compiled.Regions.Measurement, Is.EqualTo(measurement));
        });
    }

    [Test]
    public void Compile_RunsOneRegionAnalysisPerRequestOfANestedFamily()
    {
        using var child = new RectangleRenderNode(s_bounds, Brushes.Resource.White, null);
        using var parent = new NestedTargetParentNode(child);
        using var request = new RenderRequest(Options());
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        var compiler = new RenderRequestCompiler();

        using CompiledRenderRequest compiled = compiler.Compile(request, graph);

        Assert.Multiple(() =>
        {
            Assert.That(graph.NestedRequests, Has.Length.EqualTo(1));
            Assert.That(compiler.RegionAnalysisCount, Is.EqualTo(2));
            Assert.That(compiled.NestedRequests, Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void CompileAfterMetadata_RunsOneRegionAnalysisPerRequestOfANestedFamily()
    {
        using var child = new RectangleRenderNode(s_bounds, Brushes.Resource.White, null);
        using var parent = new NestedTargetParentNode(child);
        using var request = new RenderRequest(Options());
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(parent);
        var compiler = new RenderRequestCompiler();
        RenderNodeMeasurement measurement = compiler.ResolveMetadata(request, graph);

        using CompiledRenderRequest compiled = compiler.CompileAfterMetadata(request, graph, measurement);

        Assert.That(compiler.RegionAnalysisCount, Is.EqualTo(2));
        Assert.That(compiled.NestedRequests, Has.Length.EqualTo(1));
    }

    private static RenderRequestOptions Options(
        RenderRequestPurpose purpose = RenderRequestPurpose.Frame)
        => new(
            RenderIntent.Preview,
            purpose,
            targetDomain: s_bounds,
            requestedRegion: s_bounds,
            cachePolicy: RenderCacheOptions.Disabled);

    private sealed class NestedTargetParentNode(RenderNode child) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => _ = context.RecordNestedTarget(child, s_bounds);
    }
}
