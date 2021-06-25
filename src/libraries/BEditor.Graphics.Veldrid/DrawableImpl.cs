using System;

namespace BEditor.Graphics.Veldrid
{
    public abstract class DrawableImpl : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                Dispose(true);

                GC.SuppressFinalize(this);

                IsDisposed = true;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}