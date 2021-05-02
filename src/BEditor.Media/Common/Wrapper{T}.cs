using System;

namespace BEditor.Media.Common
{
    /// <summary>
    /// A base class for wrappers of unmanaged objects with <see cref="IDisposable"/> implementation.
    /// </summary>
    /// <typeparam name="T">The type of the unmanaged object.</typeparam>
    internal abstract unsafe class Wrapper<T> : IDisposable where T : unmanaged
    {
        private IntPtr _pointer;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="Wrapper{T}"/> class.
        /// </summary>
        /// <param name="pointer">A pointer to a unmanaged object.</param>
        protected Wrapper(T* pointer)
        {
            _pointer = new IntPtr(pointer);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="Wrapper{T}"/> class.
        /// </summary>
        ~Wrapper()
        {
            Disposing();
        }

        /// <summary>
        /// Gets a pointer to the underlying object.
        /// </summary>
        public T* Pointer => _isDisposed ? null : (T*)_pointer;

        /// <inheritdoc/>
        public void Dispose()
        {
            Disposing();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Updates the pointer to the object.
        /// </summary>
        /// <param name="newPointer">The new pointer.</param>
        protected void UpdatePointer(T* newPointer)
        {
            _pointer = new IntPtr(newPointer);
        }

        /// <summary>
        /// Free the unmanaged resources.
        /// </summary>
        protected abstract void OnDisposing();

        private void Disposing()
        {
            if (_isDisposed)
            {
                return;
            }

            OnDisposing();

            _isDisposed = true;
        }
    }
}