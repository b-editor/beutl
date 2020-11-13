using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

using BEditor.Core.Media;

namespace BEditor.Models.Extension
{
    public static class ColorConverter
    {
        public static Brush ToBrush(this Core.Media.Color color) => new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)color.A, (byte)color.R, (byte)color.G, (byte)color.B));
    }
}
