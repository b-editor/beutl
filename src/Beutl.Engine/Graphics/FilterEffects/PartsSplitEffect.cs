using System.ComponentModel.DataAnnotations;
using System.Reactive;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.PartsSplitEffect), ResourceType = typeof(GraphicsStrings))]
public partial class PartsSplitEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.CustomEffect(Unit.Default, ApplyCore);
    }

    private void ApplyCore(Unit unit, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            RenderTarget srcRenderTarget = target.RenderTarget!;
            using var src = srcRenderTarget.Snapshot();

            // 輪郭検出（階層付き）
            ContourTracer.FindContoursWithHierarchy(src, out var points, out var parentIndices);
            using (points)
            using (parentIndices)
            {
                var newTargets = new EffectTargets();

                try
                {
                    var pathes = new List<(SKPath Path, int Parent, int Index)>(points.Count);
                    for (int i1 = 0; i1 < points.Count; i1++)
                    {
                        ReadOnlySpan<PixelPoint> inner = points[i1];
                        int parent = parentIndices[i1];
                        var skpath = new SKPath();
                        for (int j = 0; j < inner.Length; j++)
                        {
                            if (j == 0)
                                skpath.MoveTo(inner[j].X, inner[j].Y);
                            else
                                skpath.LineTo(inner[j].X, inner[j].Y);
                        }

                        skpath.Close();
                        pathes.Add((skpath, parent, i1));
                    }

                    for (int j = 0; j < pathes.Count; j++)
                    {
                        (SKPath Path, int Parent, int Index) item = pathes[j];
                        if (0 <= item.Parent)
                        {
                            int parentIndex = pathes.FindIndex(v => v.Index == item.Parent);
                            if (parentIndex >= 0)
                            {
                                (SKPath, int Parent, int Index) parent = pathes[parentIndex];
                                SKPath? newPath = parent.Item1.Op(item.Path, SKPathOp.Xor);
                                if (newPath != null)
                                {
                                    item.Path.Dispose();
                                    parent.Item1.Dispose();
                                    pathes[parentIndex] = (newPath, parent.Parent, parent.Index);

                                    pathes.RemoveAt(j);
                                    if (parentIndex < j)
                                    {
                                        j--;
                                    }
                                }
                            }
                        }
                    }

                    // feature 003: contours are traced in the source buffer's DEVICE pixels (the buffer is
                    // ceil(bounds × w) when w != 1), so convert path bounds back to LOGICAL units (/ w)
                    // before composing them with the logical target bounds; CreateTarget re-densifies by w.
                    // The clip + draw below stay in device px (the source buffer and skpath share that space).
                    // The bare (nominal) WorkingScale is clamp-safe here: each per-part bounds is one traced
                    // contour of the already-allocatable source (pathBounds ÷ w only ever sub-crops, never
                    // inflates), so CreateTarget's FR-037(b) clamp returns w unchanged — newTarget.Scale.Value
                    // always equals w on this path, unlike the bounds-inflating FlatShadow/Displacement effects.
                    float w = context.WorkingScale;
                    foreach ((SKPath skpath, _, _) in pathes)
                    {
                        SKRect pathBounds = skpath.TightBounds;
                        var bounds = new Rect(
                            target.Bounds.X + pathBounds.Left / w,
                            target.Bounds.Y + pathBounds.Top / w,
                            pathBounds.Width / w,
                            pathBounds.Height / w);
                        EffectTarget newTarget = context.CreateTarget(bounds);
                        // feature 003: the clip path, its bounds translation and the source blit are all DEVICE px
                        // (the skpath was traced in the device buffer), so enter absolute device space — Open's
                        // base CTM CreateScale(w) would otherwise re-scale them. w == 1 = no-op.
                        using (ImmediateCanvas newCanvas = context.Open(newTarget))
                        using (newCanvas.PushDeviceSpace())
                        using (newCanvas.PushTransform(Matrix.CreateTranslation(-pathBounds.Left, -pathBounds.Top)))
                        {
                            newCanvas.Clear();
                            newCanvas.Canvas.ClipPath(skpath, antialias: true);

                            newCanvas.DrawRenderTarget(srcRenderTarget, default);
                        }

                        newTargets.Add(newTarget);

                        skpath.Dispose();
                    }

                    srcRenderTarget.Dispose();
                    context.Targets.RemoveAt(i);
                    context.Targets.InsertRange(i, newTargets);
                    i += newTargets.Count - 1;
                }
                catch
                {
                    newTargets.Dispose();
                }
            }
        }
    }
}
