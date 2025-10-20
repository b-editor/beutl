using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Rendering;

internal sealed class FpsText
{
    private static readonly Brush.Resource s_background = new SolidColorBrush(Colors.Black, 50).ToResource(RenderContext.Default);
    private double _maxFps;
    private double _minFps = double.MaxValue;
    private double _avgFps;
    private double _prevFps;
    private readonly FormattedText[] _texts;

    public FpsText()
    {
        _texts = new FormattedText[4];
        var white = Brushes.Resource.White;
        for (int i = 0; i < 4; i++)
        {
            _texts[i] = new FormattedText();
            _texts[i].Size = 72;
            _texts[i].Brush = white;
        }
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

                fpsText._texts[0].Text = $"{fps:N2} FPS";
                fpsText._texts[1].Text = $"Min: {fpsText._minFps:N2} FPS";
                fpsText._texts[2].Text = $"Max: {fpsText._maxFps:N2} FPS";
                fpsText._texts[3].Text = $"Avg: {fpsText._avgFps:N2} FPS";

                var bounds = Rect.Empty;
                foreach (var text in fpsText._texts)
                {
                    bounds = bounds.Union(text.Bounds);
                }

                canvas.DrawRectangle(bounds, s_background, null);

                float y = 0f;
                foreach (var text in fpsText._texts)
                {
                    using (canvas.PushTransform(Matrix.CreateTranslation(0, y)))
                    {
                        canvas.DrawText(text, text.Brush!, text.Pen);
                    }
                    y += text.Bounds.Height;
                }
            }
        }
    }
}
