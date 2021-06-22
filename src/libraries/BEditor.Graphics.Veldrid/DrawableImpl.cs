using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Veldrid
{
    public abstract class DrawableImpl : IDrawableImpl
    {
        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public Color Color { get; set; } = Colors.White;

        /// <inheritdoc/>
        public Material Material { get; set; } = new(Colors.White, Colors.White, Colors.White, 16);

        /// <inheritdoc/>
        public BlendMode BlendMode { get; set; }

        /// <inheritdoc/>
        public Transform Transform { get; set; } = Transform.Default;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                Dispose(true);

                GC.SuppressFinalize(this);

                IsDisposed = true;
            }
        }

        /// <inheritdoc cref="IDisposable.Dispose"/>
        protected virtual void Dispose(bool disposing)
        {
        }
    }
}
