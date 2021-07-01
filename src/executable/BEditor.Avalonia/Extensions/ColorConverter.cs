namespace BEditor.Extensions
{
    public static class ColorConverter
    {
        public static Avalonia.Media.Color ToAvalonia(this Drawing.Color color)
        {
            return Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        public static Drawing.Color ToDrawing(this Avalonia.Media.Color color)
        {
            return Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
}