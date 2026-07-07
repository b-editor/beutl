using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.PartsSplitEffect), ResourceType = typeof(GraphicsStrings))]
public partial class PartsSplitEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        // The part count is contour-discovered at execution time — a dynamic-outputs split (C3.5): the executor
        // allocates each part's pooled target at runtime, counts it, and releases it within the frame.
        builder.Split(SplitNodeDescriptor.Dynamic(EmitParts, structuralToken: nameof(PartsSplitEffect)));
    }

    private static void EmitParts(ISplitEmitter emitter)
    {
        EffectInput input = emitter.Input;
        float w = emitter.WorkingScale;
        using Bitmap src = input.Snapshot();

        ContourTracer.FindContoursWithHierarchy(src, out var points, out var parentIndices);
        using (points)
        using (parentIndices)
        {
            var pathes = new List<(SKPath Path, int Parent, int Index)>(points.Count);
            try
            {
                for (int i = 0; i < points.Count; i++)
                {
                    ReadOnlySpan<PixelPoint> inner = points[i];
                    int parent = parentIndices[i];
                    var skpath = new SKPath();
                    for (int j = 0; j < inner.Length; j++)
                    {
                        if (j == 0)
                            skpath.MoveTo(inner[j].X, inner[j].Y);
                        else
                            skpath.LineTo(inner[j].X, inner[j].Y);
                    }

                    skpath.Close();
                    pathes.Add((skpath, parent, i));
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

                foreach ((SKPath skpath, _, _) in pathes)
                {
                    SKRect pathBounds = skpath.TightBounds;
                    var bounds = new Rect(
                        input.Bounds.X + pathBounds.Left / w,
                        input.Bounds.Y + pathBounds.Top / w,
                        pathBounds.Width / w,
                        pathBounds.Height / w);

                    SKPath capturedPath = skpath;
                    emitter.Emit(bounds, session =>
                    {
                        ImmediateCanvas canvas = session.OpenCanvas();
                        using (canvas.PushDeviceSpace())
                        using (canvas.PushTransform(Matrix.CreateTranslation(-pathBounds.Left, -pathBounds.Top)))
                        {
                            canvas.Canvas.ClipPath(capturedPath, antialias: true);
                            input.Draw(canvas, default);
                        }
                    });
                }
            }
            finally
            {
                foreach ((SKPath skpath, _, _) in pathes)
                    skpath.Dispose();
            }
        }
    }
}
