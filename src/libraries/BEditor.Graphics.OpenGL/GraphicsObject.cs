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
using BEditor.Graphics.OpenGL.Resources;
using BEditor.Graphics.Platform;

namespace BEditor.Graphics.OpenGL
{
    /// <summary>
    /// Represents the OpenGL object.
    /// </summary>
    public abstract class GraphicsObject : IDrawableImpl
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
        public abstract float[] Vertices { get; }

        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public Color Color { get; set; } = Colors.White;

        /// <inheritdoc/>
        public Material Material { get; set; } = new(Colors.White, Colors.White, Colors.White, 16);

        /// <inheritdoc/>
        public BlendMode BlendMode { get; set; }

        /// <inheritdoc/>
        public RasterizerState RasterizerState { get; set; } = RasterizerState.CullNone;

        /// <inheritdoc/>
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