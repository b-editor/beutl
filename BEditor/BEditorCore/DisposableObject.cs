using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace BEditorCore {
    public abstract class DisposableObject : IDisposable {
        public bool IsDisposed { get; private set; }

        public void Dispose() {
            OnDispose(true);
            GC.SuppressFinalize(this);
        }

        ~DisposableObject() {
            OnDispose(false);
        }

        protected abstract void OnDispose(bool disposing);

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        public void ThrowIfDisposed() {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }
    }

    public class DisposableCollection : List<IDisposable>, ICollection<IDisposable>, IEnumerable<IDisposable>, IList<IDisposable>, IReadOnlyCollection<IDisposable>, IReadOnlyList<IDisposable>, IDisposable {
        public void Dispose() {
            foreach(var disposable in this) {
                disposable.Dispose();
            }
        }
    }
}
