using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Extensions.AviUtl
{
    public struct RandomStruct
    {
        private int x;
        private int y;
        private int z;
        private int w;

        public RandomStruct(int seed)
        {
            x = 12345 + seed;
            y = 67890 + seed;
            z = 98765 + seed;
            w = 43210 + seed;
        }

        public int Next(int min, int max)
        {
            if (min >= max) return min;
            var t = x ^ (x << 11);
            x = y;
            y = z;
            z = w;
            w = w ^ (w >> 19) ^ (t ^ (t >> 8));
            return w % (max - min) + min;
        }
    }
}