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
        context.CustomEffect(Unit.Default, ApplyCore, static (_, bounds) => bounds);
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

                    float w = target.Scale.Value;
                    Vector rasterOrigin = target.RasterBounds.Position - target.Bounds.Position;
                    SKMatrix sourceToLogical = SKMatrix.CreateScaleTranslation(
                        1f / w,
                        1f / w,
                        (float)rasterOrigin.X,
                        (float)rasterOrigin.Y);
                    foreach ((SKPath skpath, _, _) in pathes)
                    {
                        skpath.Transform(sourceToLogical);
                        SKRect pathBounds = skpath.TightBounds;
                        var bounds = new Rect(
                            target.Bounds.X + pathBounds.Left,
                            target.Bounds.Y + pathBounds.Top,
                            pathBounds.Width,
                            pathBounds.Height);
                        EffectTarget newTarget = context.CreateTarget(bounds);
                        Vector outputOrigin = target.Bounds.Position - newTarget.Bounds.Position;
                        using (ImmediateCanvas newCanvas = context.Open(newTarget))
                        {
                            newCanvas.Clear();
                            using (newCanvas.PushTransform(
                                       Matrix.CreateTranslation(outputOrigin.X, outputOrigin.Y)))
                            {
                                newCanvas.Canvas.ClipPath(skpath, antialias: true);

                                target.Draw(newCanvas);
                            }
                        }

                        newTargets.Add(newTarget);

                        skpath.Dispose();
                    }

                    target.Dispose();
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
