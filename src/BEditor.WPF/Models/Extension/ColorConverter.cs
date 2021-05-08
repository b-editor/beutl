using System.Windows.Media;

namespace BEditor.Models.Extension
{
    public static class ColorConverter
    {
        public static Brush ToBrush(this BEditor.Drawing.Color color) => new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)color.A, (byte)color.R, (byte)color.G, (byte)color.B));
    }
}