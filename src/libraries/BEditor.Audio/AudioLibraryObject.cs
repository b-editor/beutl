using System;

namespace BEditor.Audio
{
    [Obsolete("To be addded.")]
    public abstract class AudioLibraryObject : IDisposable
    {
        ~AudioLibraryObject()
        {
            Dispose();
        }

        public abstract int Handle { get; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;

            OnDispose();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        protected void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        protected abstract void OnDispose();
    }
}