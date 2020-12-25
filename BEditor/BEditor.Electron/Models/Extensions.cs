using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Media;

namespace BEditor.Models
{
    public static class Extensions
    {
        const int width = 10;
        public static double ToPixel(this Scene self, int number)
            => width * (self.TimeLineZoom / 200) * number;
        public static Frame ToFrame(this Scene self, double pixel)
            => (Frame)(pixel / (width * (self.TimeLineZoom / 200)));
    }
}
