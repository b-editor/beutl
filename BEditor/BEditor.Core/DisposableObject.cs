using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace BEditor.Core
{
    public abstract class DisposableObject : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
                OnDispose(true);
            GC.SuppressFinalize(this);
        }

        ~DisposableObject()
        {
            if (!IsDisposed)
                OnDispose(false);
        }

        protected abstract void OnDispose(bool disposing);

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        public void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }
    }
}
