// ComputeObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Compute
{
    /// <summary>
    /// Represents the OpenCL object.
    /// </summary>
    public abstract class ComputeObject : IDisposable
    {
        /// <summary>
        /// Finalizes an instance of the <see cref="ComputeObject"/> class.
        /// </summary>
        ~ComputeObject()
        {
            Dispose(false);
        }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// If this object is disposed, then ObjectDisposedException is thrown.
        /// </summary>
        public void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                Dispose(disposing: true);

                IsDisposed = true;
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources.
        /// </summary>
        /// <param name="disposing">
        /// If disposing equals true, the method has been called directly or indirectly by a user's code. Managed and unmanaged resources can be disposed.
        /// If false, the method has been called by the runtime from inside the finalizer and you should not reference other objects. Only unmanaged resources can be disposed.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
        }
    }
}