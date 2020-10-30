using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.NET.Media;

namespace BEditor.Models.Extension {
    public static class PointConverter {
        public static System.Windows.Point ToWin(this Point2 point) => new System.Windows.Point(point.X, point.Y);
        public static Point2 ToMedia(this System.Windows.Point point) => new Point2(point.X, point.Y);
    }
}
