
using System;

using BEditor.Drawing.Pixel;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing.Process
{
    public readonly unsafe struct NoiseProcess : IPixelProcess
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte _value;
        private readonly Random _rand;

        public NoiseProcess(BGRA32* src, BGRA32* dst, byte value, Random random)
        {
            _src = src;
            _dst = dst;
            _value = value;
            _rand = random;
        }

        public readonly void Invoke(int pos)
        {
            // ランダム値の発生
            var rnd = _rand.Next(-(_value >> 1), _value);

            _dst[pos].R = (byte)Set255(_src[pos].R + rnd);
            _dst[pos].G = (byte)Set255(_src[pos].G + rnd);
            _dst[pos].B = (byte)Set255(_src[pos].B + rnd);
            _dst[pos].A = _src[pos].A;
        }
    }
}
