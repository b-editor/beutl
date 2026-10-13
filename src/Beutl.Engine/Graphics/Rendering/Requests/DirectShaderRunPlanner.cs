using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics.Rendering.Requests;

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
        ImmediateCanvas destination,
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
        if (!ImmediateCanvas.CanDrawPixelAligned(
                rasterBounds,
                density,
                outputDeviceBounds.Size,
                destination.Density,
                destination.Transform))
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
