using BeUtl.Graphics;
using BeUtl.Graphics.Shapes;

namespace BeUtl.Rendering;

internal sealed class FpsText
{
    private double _maxFps;
    private double _minFps = double.MaxValue;
    private double _avgFps;
    private double _prevFps;
    private readonly TextElement _fpsText = new() { Size = 72 };
    private readonly TextElement _minFpsText = new() { Size = 72 };
    private readonly TextElement _maxFpsText = new() { Size = 72 };
    private readonly TextElement _avgFpsText = new() { Size = 72 };
    private readonly TextBlock _fpsFText;

    public FpsText()
    {
        _fpsFText = new TextBlock
        {
            Elements = new TextElements(new TextElement[]
            {
                _fpsText,
                _minFpsText,
                _maxFpsText,
                _avgFpsText,
            })
        };
    }

    public bool DrawFps { get; set; } = false;

    public FpsDrawer StartRender(IRenderer renderer)
    {
        return new FpsDrawer(renderer.Graphics, this);
    }

    public readonly struct FpsDrawer : IDisposable
    {
        private readonly ICanvas _canvas;
        private readonly FpsText _fpsText;
        private readonly DateTime _startTime;

        public FpsDrawer(ICanvas canvas, FpsText fpsText)
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

                _fpsText._fpsText.Text = $"{fps:N2} FPS\n";
                _fpsText._minFpsText.Text = $"Min: {_fpsText._minFps:N2} FPS\n";
                _fpsText._maxFpsText.Text = $"Max: {_fpsText._maxFps:N2} FPS\n";
                _fpsText._avgFpsText.Text = $"Avg: {_fpsText._avgFps:N2} FPS";

                _fpsText._fpsFText.Measure(_canvas.Size.ToSize(1));
                using (_canvas.PushClip(_fpsText._fpsFText.Bounds))
                {
                    _canvas.Clear();
                    _fpsText._fpsFText.Draw(_canvas);
                }
            }
        }
    }
}
