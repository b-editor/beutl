using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

using BEditor.Core.Media;

namespace BEditor.Models.Extension {
    public static class ColorConverter {
        public static Brush ToBrush(this Color3 color) => new SolidColorBrush(Color.FromRgb((byte)color.R, (byte)color.G, (byte)color.B));
        public static Brush ToBrush(this Color4 color) => new SolidColorBrush(Color.FromArgb((byte)color.A, (byte)color.R, (byte)color.G, (byte)color.B));
    }
}
