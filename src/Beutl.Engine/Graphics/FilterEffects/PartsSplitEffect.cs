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
                Cv.RetrievalModes.List,
                Cv.ContourApproximationModes.ApproxSimple);

            var newTargets = new EffectTargets();

            try
            {
                using var skpath = new SKPath();
                foreach (Cv.Point[] inner in points)
                {
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

                    skpath.Reset();
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
