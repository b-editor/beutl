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
                canvas._canvas.RestoreToCount(Count);
                canvas._currentTransform = canvas._canvas.TotalMatrix.ToMatrix();
            }
        }

        internal record MaskPushedState(int Count, bool Invert, SKPaint Paint) : CanvasPushedState
        {
            public override void Pop(ImmediateCanvas canvas)
            {
                canvas._sharedFillPaint.Reset();
                canvas._sharedFillPaint.BlendMode = Invert ? SKBlendMode.DstOut : SKBlendMode.DstIn;

                canvas._canvas.SaveLayer(canvas._sharedFillPaint);
                using (SKPaint maskPaint = Paint)
                {
                    canvas._canvas.DrawPaint(maskPaint);
                }

                canvas._canvas.Restore();

                canvas._canvas.RestoreToCount(Count);
            }
        }

        internal record BlendModePushedState(BlendMode BlendMode) : CanvasPushedState
        {
            public override void Pop(ImmediateCanvas canvas)
            {
                canvas.BlendMode = BlendMode;
            }
        }

        public abstract void Pop(ImmediateCanvas canvas);
    }
}
