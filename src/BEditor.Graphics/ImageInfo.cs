using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Graphics
{
    public class ImageInfo : IDisposable
    {
        private readonly Func<ImageInfo, Transform> _getTransform;

        public ImageInfo(Image<BGRA32> image, Func<ImageInfo, Transform> transform, int index)
        {
            Source = image;
            _getTransform = transform;
            Index = index;
        }

        public Image<BGRA32> Source { get; set; }
        public int Index { get; }
        public Transform Transform => _getTransform(this);
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;

            Source.Dispose();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}
