using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct DirectRenderTargetGeometry(float Density, Matrix Transform)
{
    public static DirectRenderTargetGeometry FromCanvas(ImmediateCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        return new DirectRenderTargetGeometry(canvas.Density, canvas.Transform);
    }

    public bool CanDrawPixelAligned(Rect destination, float sourceDensity, PixelSize sourceSize)
        => ImmediateCanvas.CanDrawPixelAligned(
            destination,
            sourceDensity,
            sourceSize,
            Density,
            Transform);
}

internal readonly record struct DirectShaderRunPlan(
    Rect OutputBounds,
    Rect RequiredRegion,
    PixelRect OutputDeviceBounds,
    Rect RasterBounds,
    float Density);

internal static class DirectShaderRunPlanner
{
    public static bool TryResolve(
        RenderFragmentReference fragment,
        CompiledShaderRun run,
        RecordedRenderGraph graph,
        RegionAnalysis regions,
        DirectRenderTargetGeometry destination,
        out DirectShaderRunPlan plan)
    {
        plan = default;
        RenderFragmentReference output = run.GetOutput(graph);
        if (!ReferenceEquals(output, fragment))
            return false;

        Rect outputBounds = output.Bounds;
        RenderFragmentReference requirementFragment = run.GetWholeSourceHead(graph) is null
            ? output
            : run.GetStage(graph, 0);
        Rect requiredRegion = regions.GetFragmentRequirement(requirementFragment).Resolve(outputBounds);
        if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
            return false;

        float requestedDensity = fragment.EffectiveScale.IsUnbounded
            ? destination.Density
            : fragment.EffectiveScale.Value;
        float density = BufferDimensionBudget.EngineCeiling.ClampWorkingScale(
            outputBounds,
            requestedDensity);
        if (density != destination.Density)
            return false;

        PixelRect outputDeviceBounds = PixelRect.FromRect(requiredRegion, density);
        Rect rasterBounds = outputDeviceBounds.ToRect(density);
        if (!destination.CanDrawPixelAligned(
                rasterBounds,
                density,
                outputDeviceBounds.Size))
        {
            return false;
        }

        plan = new DirectShaderRunPlan(
            outputBounds,
            requiredRegion,
            outputDeviceBounds,
            rasterBounds,
            density);
        return true;
    }
}
