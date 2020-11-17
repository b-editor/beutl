using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using BEditor.Core.Extensions;
using BEditor.Core.Extensions.ViewCommand;

namespace BEditor.Core
{
    public abstract class DisposableObject : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
                OnDispose(true);
            else
            {
                var str = $"{GetType().Name} が二重廃棄されようとしました";
                Message.Snackbar(str);

#if DEBUG
                ActivityLog.ErrorLogStackTrace(new Exception(str), Environment.StackTrace);
#endif
            }

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
