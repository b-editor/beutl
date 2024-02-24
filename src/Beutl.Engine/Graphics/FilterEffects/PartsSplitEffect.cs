using System.Reactive;

using SkiaSharp;

using Cv = OpenCvSharp;

namespace Beutl.Graphics.Effects;

public class PartsSplitEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect(Unit.Default, ApplyCore);
    }

    private void ApplyCore(Unit unit, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            Media.Source.Ref<SKSurface> srcSurface = target.Surface!;
            using SKImage skimage = srcSurface.Value.Snapshot();
            using var src = skimage.ToBitmap();
            using Cv.Mat srcMat = src.ToMat();
            using Cv.Mat alphaMat = srcMat.ExtractChannel(3);

            // 輪郭検出
            alphaMat.FindContours(
                out Cv.Point[][] points,
                out Cv.HierarchyIndex[] h,
                Cv.RetrievalModes.Tree,
                Cv.ContourApproximationModes.ApproxSimple);

            var newTargets = new EffectTargets();

            try
            {
                var pathes = new List<(SKPath, int Parent, int Index)>(h.Length);
                for (int i1 = 0; i1 < points.Length; i1++)
                {
                    Cv.Point[] inner = points[i1];
                    int parent = h[i1].Parent;
                    var skpath = new SKPath();
                    bool first = true;
                    foreach (Cv.Point item in inner)
                    {
                        if (first)
                        {
                            skpath.MoveTo(item.X, item.Y);
                            first = false;
                        }
                        else
                        {
                            skpath.LineTo(item.X, item.Y);
                        }
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
                            SKPath newPath = parent.Item1.Op(item.Path, SKPathOp.Xor);
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

                foreach ((SKPath skpath, _, _) in pathes)
                {
                    SKRect pathBounds = skpath.TightBounds;
                    var bounds = new Rect(
                        target.Bounds.X + pathBounds.Left,
                        target.Bounds.Y + pathBounds.Top,
                        pathBounds.Width,
                        pathBounds.Height);
                    EffectTarget newTarget = context.CreateTarget(bounds);
                    using (ImmediateCanvas newCanvas = context.Open(newTarget))
                    using (newCanvas.PushTransform(Matrix.CreateTranslation(-pathBounds.Left, -pathBounds.Top)))
                    {
                        newCanvas.Canvas.ClipPath(skpath, antialias: true);

                        newCanvas.DrawSurface(srcSurface.Value, default);
                    }

                    newTargets.Add(newTarget);

                    skpath.Dispose();
                }

                srcSurface.Dispose();
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
