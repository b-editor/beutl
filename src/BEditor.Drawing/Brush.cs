namespace BEditor.Drawing
{
    public class Brush
    {
        public Color Color { get; set; }
        public bool IsAntialias { get; set; }
        public BrushStyle Style { get; set; }
        public int StrokeWidth { get; set; }
    }

    public enum BrushStyle
    {
        Fill,
        Stroke
    }
}
