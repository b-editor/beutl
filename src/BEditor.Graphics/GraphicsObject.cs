// GraphicsObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.ComponentModel;
using System.Threading;

using BEditor.Drawing;
using BEditor.Graphics.Resources;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the OpenGL object.
    /// </summary>
    public abstract class GraphicsObject : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsObject"/> class.
        /// </summary>
        protected GraphicsObject()
        {
            SynchronizeContext = AsyncOperationManager.SynchronizationContext ?? throw new InvalidOperationException(Strings.SynchronizationContextIsNull);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="GraphicsObject"/> class.
        /// </summary>
        ~GraphicsObject()
        {
            if (!IsDisposed)
            {
                SynchronizeContext.Send(_ => Dispose(false), null);
            }
        }

        /// <summary>
        /// Gets the vertices of this <see cref="GraphicsObject"/>.
        /// </summary>
        public abstract ReadOnlyMemory<float> Vertices { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets or sets the color of this <see cref="GraphicsObject"/>.
        /// </summary>
        public Color Color { get; set; } = Colors.White;

        /// <summary>
        /// Gets or sets the material of this <see cref="GraphicsObject"/>.
        /// </summary>
        public Material Material { get; set; } = new(Colors.White, Colors.White, Colors.White, 16);

        /// <summary>
        /// Gets or sets the transform of this <see cref="GraphicsObject"/>.
        /// </summary>
        public Transform Transform { get; set; } = Transform.Default;

        /// <summary>
        /// Gets the synchronization context associated with this object.
        /// </summary>
        protected SynchronizationContext SynchronizeContext { get; }

        /// <summary>
        /// Draw this <see cref="GraphicsObject"/>.
        /// </summary>
        public abstract void Draw();

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                SynchronizeContext.Send(_ => Dispose(true), null);

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