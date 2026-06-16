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

        // No-op pop for PushDeviceSpace when the canvas is already in device space.
        internal sealed record NoOpPushedState : CanvasPushedState
        {
            public static readonly NoOpPushedState Instance = new();

            public override void Pop(ImmediateCanvas canvas)
            {
            }
        }

        // Restores matrix, density, and base transform to the enclosing scope.
        internal record DeviceSpacePushedState(int Count, float PrevDensity, Matrix PrevBaseTransform)
            : CanvasPushedState
        {
            public override void Pop(ImmediateCanvas canvas)
            {
                canvas.Canvas.RestoreToCount(Count);
                canvas._currentTransform = canvas.Canvas.TotalMatrix.ToMatrix();
                canvas._currentDensity = PrevDensity;
                canvas._currentBaseTransform = PrevBaseTransform;
            }
        }

        internal record MaskPushedState(int Count, bool Invert, SKPaint Paint) : CanvasPushedState
        {
            public override void Pop(ImmediateCanvas canvas)
            {
                canvas._sharedFillPaint.Reset();
                canvas._sharedFillPaint.BlendMode = Invert ? SKBlendMode.DstOut : SKBlendMode.DstIn;

                canvas.Canvas.SaveLayer(canvas._sharedFillPaint);
                using (SKPaint maskPaint = Paint)
                {
                    canvas.Canvas.DrawPaint(maskPaint);
                }

                canvas.Canvas.Restore();

                canvas.Canvas.RestoreToCount(Count);
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
