namespace BEditor.Extensions
{
    public static class ColorConverter
    {
        // 'BEditor.Drawing.Color'から'Avalonia.Media.Color'に変換
        public static Avalonia.Media.Color ToAvalonia(this Drawing.Color color)
        {
            return Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        // 'Avalonia.Media.Color'から'BEditor.Drawing.Color'に変換
        public static Drawing.Color ToDrawing(this Avalonia.Media.Color color)
        {
            return Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
}