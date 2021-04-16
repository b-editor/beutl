using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.RowOperation
{
    public readonly unsafe struct CropOperation<TPixel> : IRowOperation
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Image<TPixel> _src;
        private readonly Image<TPixel> _dst;
        private readonly Rectangle _roi;

        public CropOperation(Image<TPixel> src, Image<TPixel> dst, Rectangle roi)
        {
            _src = src;
            _dst = dst;
            _roi = roi;
        }

        public readonly void Invoke(int y)
        {
            var sourceRow = _src[y + _roi.Y].Slice(_roi.X, _roi.Width);
            var targetRow = _dst[y];

            sourceRow.Slice(0, _roi.Width).CopyTo(targetRow);
        }
    }
}