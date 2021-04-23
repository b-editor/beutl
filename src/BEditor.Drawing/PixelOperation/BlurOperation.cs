using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Compute.Memory;

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct BlurOperation : IGpuPixelOperation<AbstractMemory, int>
    {
        public string GetKernel()
        {
            return "blur";
        }

        public string GetSource()
        {
            return @"
__kernel void blur(__global unsigned char* src, __global unsigned char* dst, int size)
{
    int xx = get_global_id(0);
    int yy = get_global_id(1);
    int width = get_global_size(0);
    int stride = width * 4;

    int minY = max(0, yy - size);
    int maxY = min(yy, yy + size);
    int ver = maxY - minY;

    float rAvgF = 0;
    float gAvgF = 0;
    float bAvgF = 0;
    float aAvgF = 0;
    int minX = max(0, xx - size);
    int maxX = min(width, xx + size);
    int hor = maxX - minX;

    float cnt = (float)(hor * ver);

    for (int x = minX; x < maxX; x++)
    {
        int col = x * 4;
        for (int y = minY; y < maxY; y++)
        {
            int pos = stride * y + col;

            bAvgF += src[pos] / cnt;
            gAvgF += src[pos + 1] / cnt;
            rAvgF += src[pos + 2] / cnt;
            aAvgF += src[pos + 3] / cnt;
        }
    }

    int pos = stride * yy + xx * 4;

    dst[pos] = bAvgF;
    dst[pos + 1] = gAvgF;
    dst[pos + 2] = rAvgF;
    dst[pos + 3] = aAvgF;
}";
        }
    }
}
