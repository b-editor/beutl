using System;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.RowOperation
{
    public readonly unsafe struct ReplaceOperation<TPixel> : IRowOperation
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Image<TPixel> _src;
        private readonly Image<TPixel> _dst;
        private readonly Rectangle _roi;

        public ReplaceOperation(Image<TPixel> src, Image<TPixel> dst, Rectangle roi)
        {
            _src = src;
            _dst = dst;
            _roi = roi;
        }

        public readonly void Invoke(int y)
        {
            var sourceRow = _src[y];
            var targetRow = _dst[y + _roi.Y].Slice(_roi.X, _roi.Width);

            sourceRow.CopyTo(targetRow);
        }
    }
}