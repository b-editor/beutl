using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    private abstract record CanvasPushedState
    {
        internal record SKCanvasPushedState(int Count) : CanvasPushedState
        {
            public override void Pop(ImmediateCanvas canvas)
            {
                canvas.Canvas.RestoreToCount(Count);
                canvas._currentTransform = canvas.Canvas.TotalMatrix.ToMatrix();
            }
        }

        internal record BlendModePushedState(BlendMode BlendMode, int Count, SKPaint Paint) : CanvasPushedState
        {
            public override void Pop(ImmediateCanvas canvas)
            {
                canvas.Canvas.RestoreToCount(Count);
                canvas.BlendMode = BlendMode;
                Paint.Dispose();
            }
        }

        internal record OpacityPushedState(float Opacity, int Count, SKPaint Paint) : CanvasPushedState
        {
            public override void Pop(ImmediateCanvas canvas)
            {
                canvas._sharedFillPaint.Reset();
                canvas._sharedFillPaint.BlendMode = SKBlendMode.DstIn;

                canvas.Canvas.SaveLayer(canvas._sharedFillPaint);
                using (SKPaint maskPaint = Paint)
                {
                    canvas.Canvas.DrawPaint(maskPaint);
                }

                canvas.Canvas.Restore();

                canvas.Canvas.RestoreToCount(Count);
                canvas.Opacity = Opacity;
            }
        }

        public abstract void Pop(ImmediateCanvas canvas);
    }
}
