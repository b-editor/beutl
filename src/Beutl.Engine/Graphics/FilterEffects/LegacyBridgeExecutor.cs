using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Executes an <see cref="OpaqueLegacyNodeDescriptor"/>'s recorded item list through the retained (internal-only)
/// activator machinery (feature 004, rollout step 3, research D10). This is the pre-redesign
/// <c>FilterEffectRenderNode.Process</c> tail moved verbatim behind the new describe→compile→execute seam: for a
/// bridged effect the rendered output is <b>byte-identical</b> to today's, and — because every counter increment
/// still happens inside <see cref="FilterEffectActivator"/> / <see cref="CustomFilterEffectContext"/> — the
/// counter attribution is unchanged (the opaque pass itself is never additionally counted as a GPU pass). Deleted
/// with the bridge in step 6.
/// </summary>
internal static class LegacyBridgeExecutor
{
    public static RenderNodeOperation[] Execute(
        FilterEffectContext feContext,
        RenderNodeOperation[] inputs,
        Rect bounds,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        PipelineDiagnostics? diagnostics,
        RenderTargetPool? pool)
    {
        var effectTargets = new EffectTargets();
        effectTargets.AddRange(inputs.Select(i => new EffectTarget(i)));

        using (var builder = new SKImageFilterBuilder())
        using (var activator = new FilterEffectActivator(
                   effectTargets, builder, outputScale, workingScale, maxWorkingScale, diagnostics, pool))
        {
            activator.Apply(feContext);

            if (builder.HasFilter())
            {
                SKImageFilter? imageFilter = builder.GetFilter();
                return activator.CurrentTargets.Select(t =>
                {
                    var paint = new SKPaint();
                    paint.ImageFilter = imageFilter;
                    return RenderNodeOperation.CreateLambda(
                        bounds: t.Bounds,
                        render: canvas =>
                        {
                            using (canvas.PushBlendMode(BlendMode.SrcOver))
                            using (canvas.PushTransform(Matrix.CreateTranslation(
                                       t.Bounds.X - t.OriginalBounds.X,
                                       t.Bounds.Y - t.OriginalBounds.Y)))
                            using (canvas.PushPaint(paint))
                            {
                                t.Draw(canvas);
                            }
                        },
                        hitTest: t.Bounds.Contains,
                        onDispose: () =>
                        {
                            t.Dispose();
                            paint.Dispose();
                        },
                        effectiveScale: t.Scale
                    );
                }).ToArray();
            }

            return activator.CurrentTargets.Select(i =>
                    i.NodeOperation ??
                    RenderNodeOperation.CreateFromRenderTarget(i.Bounds, i.Bounds.Position, i.RenderTarget!, i.Scale))
                .ToArray();
        }
    }
}
