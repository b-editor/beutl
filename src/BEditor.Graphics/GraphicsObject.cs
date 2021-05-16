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
        /// Discards the reference to the target that is represented by the current <see cref="GraphicsObject"/> object.
        /// </summary>
        ~GraphicsObject()
        {
            if (IsDisposed) return;

            SynchronizeContext.Send(_ => Dispose(false), null);
        }

        /// <summary>
        /// Gets the synchronization context associated with this object.
        /// </summary>
        protected SynchronizationContext SynchronizeContext { get; }

        /// <summary>
        /// Gets the vertices of this <see cref="GraphicsObject"/>.
        /// </summary>
        public abstract ReadOnlyMemory<float> Vertices { get; }

        /// <summary>
        /// Gets whether an object has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets the color of this <see cref="GraphicsObject"/>.
        /// </summary>
        public Color Color { get; set; } = Color.Light;

        /// <summary>
        /// Gets the material of this <see cref="GraphicsObject"/>.
        /// </summary>
        public Material Material { get; set; } = new(Color.Light, Color.Light, Color.Light, 16);

        /// <summary>
        /// Gets the transform of this <see cref="GraphicsObject"/>.
        /// </summary>
        public Transform Transform { get; set; } = Transform.Default;

        /// <summary>
        /// Draw this <see cref="GraphicsObject"/>.
        /// </summary>
        public abstract void Draw();

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            SynchronizeContext.Send(_ => Dispose(true), null);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        /// <inheritdoc cref="IDisposable.Dispose"/>
        protected virtual void Dispose(bool disposing)
        {
        }
    }
}