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

    public readonly struct FpsDrawer : IDisposable
    {
        private readonly ImmediateCanvas _canvas;
        private readonly FpsText _fpsText;
        private readonly DateTime _startTime;

        public FpsDrawer(ImmediateCanvas canvas, FpsText fpsText)
        {
            _canvas = canvas;
            _fpsText = fpsText;
            _startTime = DateTime.Now;
        }

        public void Dispose()
        {
            if (_fpsText.DrawFps)
            {
                DateTime endTime = DateTime.Now;

                double sec = (double)(endTime - _startTime).TotalSeconds;
                double fps = 1 / sec;
                _fpsText._maxFps = Math.Max(_fpsText._maxFps, fps);
                _fpsText._minFps = Math.Min(_fpsText._minFps, fps);

                _fpsText._prevFps = fps;
                _fpsText._avgFps = (_fpsText._prevFps + fps) / 2;

                _fpsText._textBlock.Text = $"""
                    {fps:N2} FPS
                    Min: {_fpsText._minFps:N2} FPS
                    Max: {_fpsText._maxFps:N2} FPS
                    Avg: {_fpsText._avgFps:N2} FPS
                    """;

                _fpsText._textBlock.Measure(_canvas.Size.ToSize(1));
                float width = _fpsText._textBlock.Bounds.Size.Width;
                float height = _fpsText._textBlock.Bounds.Size.Height;

                using (_canvas.PushTransform(Matrix.CreateTranslation(width / 2, height / 2)))
                {
                    _canvas.DrawRectangle(_fpsText._textBlock.Bounds, s_background, null);
                    _fpsText._textBlock.Render(_canvas);
                }
            }
        }
    }
}
