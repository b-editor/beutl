using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Immutable;

namespace Beutl.Rendering;

internal sealed class FpsText
{
    private static readonly IBrush s_background = new ImmutableSolidColorBrush(Colors.Black, 50);
    private double _maxFps;
    private double _minFps = double.MaxValue;
    private double _avgFps;
    private double _prevFps;
    private readonly TextBlock _textBlock;

    public FpsText()
    {
        _textBlock = new TextBlock
        {
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            Size = 72,
            Fill = Brushes.White
        };
    }

    public bool DrawFps { get; set; } = false;

    public FpsDrawer StartRender(ImmediateCanvas canvas)
    {
        return new FpsDrawer(canvas, this);
    }

    public readonly struct FpsDrawer(ImmediateCanvas canvas, FpsText fpsText) : IDisposable
    {
        private readonly DateTime _startTime = DateTime.Now;

        public void Dispose()
        {
            if (fpsText.DrawFps)
            {
                DateTime endTime = DateTime.Now;

                double sec = (double)(endTime - _startTime).TotalSeconds;
                double fps = 1 / sec;
                fpsText._maxFps = Math.Max(fpsText._maxFps, fps);
                fpsText._minFps = Math.Min(fpsText._minFps, fps);

                fpsText._prevFps = fps;
                fpsText._avgFps = (fpsText._prevFps + fps) / 2;

                fpsText._textBlock.Text = $"""
                    {fps:N2} FPS
                    Min: {fpsText._minFps:N2} FPS
                    Max: {fpsText._maxFps:N2} FPS
                    Avg: {fpsText._avgFps:N2} FPS
                    """;

                fpsText._textBlock.Measure(canvas.Size.ToSize(1));
                float width = fpsText._textBlock.Bounds.Size.Width;
                float height = fpsText._textBlock.Bounds.Size.Height;

                using (canvas.PushTransform(Matrix.CreateTranslation(width / 2, height / 2)))
                {
                    canvas.DrawRectangle(fpsText._textBlock.Bounds, s_background, null);
                    fpsText._textBlock.Render(canvas);
                }
            }
        }
    }
}
