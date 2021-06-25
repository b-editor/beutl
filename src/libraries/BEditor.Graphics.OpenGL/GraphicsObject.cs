using System;
using System.Threading;

using BEditor.Graphics.OpenGL.Resources;

namespace BEditor.Graphics.OpenGL
{
    public abstract class GraphicsObject : IDisposable
    {
        protected GraphicsObject()
        {
            SynchronizeContext = GraphicsContextImpl.SyncContext ?? throw new InvalidOperationException(Strings.SynchronizationContextIsNull);
        }

        ~GraphicsObject()
        {
            if (!IsDisposed)
            {
                SynchronizeContext.Send(_ => Dispose(false), null);
            }
        }

        public abstract float[] Vertices { get; }

        public bool IsDisposed { get; private set; }

        protected SynchronizationContext SynchronizeContext { get; }

        public abstract void Draw();

        public void Dispose()
        {
            if (!IsDisposed)
            {
                SynchronizeContext.Send(_ => Dispose(true), null);

                GC.SuppressFinalize(this);

                IsDisposed = true;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}