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
    /// <summary>
    /// 
    /// </summary>
    public class ImageInfo : IDisposable, IAsyncDisposable
    {
        private readonly Func<ImageInfo, Transform> _getTransform;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="image"></param>
        /// <param name="transform"></param>
        public ImageInfo(Image<BGRA32> image, Func<ImageInfo, Transform> transform)
        {
            Source = image;
            _getTransform = transform;
        }

        /// <summary>
        /// 
        /// </summary>
        public Image<BGRA32> Source { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public Transform Transform => _getTransform(this);
        /// <summary>
        /// 
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed) return;

            Source.Dispose();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async ValueTask DisposeAsync()
        {
            if (IsDisposed) return;

            await Source.DisposeAsync();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}
