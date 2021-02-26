using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BEditor.Drawing;

namespace BEditor.Graphics
{
    public abstract class GraphicsObject : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsObject"/> class.
        /// </summary>
        public GraphicsObject()
        {
            SynchronizeContext = AsyncOperationManager.SynchronizationContext ?? throw new InvalidOperationException("現在のスレッドのSynchronizationContextがnullでした。"); ;

        }
        /// <summary>
        /// Discards the reference to the target that is represented by the current <see cref="GraphicsObject"/> object.
        /// </summary>
        ~GraphicsObject()
        {
            if (IsDisposed) return;

            SynchronizeContext.Post(_ => Dispose(false), null);
        }

        protected SynchronizationContext SynchronizeContext { get; }
        /// <summary>
        /// Get the vertices of this <see cref="GraphicsObject"/>.
        /// </summary>
        public abstract ReadOnlyMemory<float> Vertices { get; }
        /// <summary>
        /// Get whether an object has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }
        /// <summary>
        /// Get the color of this <see cref="GraphicsObject"/>.
        /// </summary>
        public Color Color { get; set; } = Color.Light;
        /// <summary>
        /// Get the material of this <see cref="GraphicsObject"/>.
        /// </summary>
        public Material Material { get; set; } = new(Color.Light, Color.Light, Color.Light, 16);
        /// <summary>
        /// Get the transform of this <see cref="GraphicsObject"/>.
        /// </summary>
        public Transform Transform { get; set; } = Transform.Default;

        /// <summary>
        /// Draw this <see cref="GraphicsObject"/>.
        /// </summary>
        public abstract void Draw();
        /// <inheritdoc cref="IDisposable.Dispose"/>
        protected virtual void Dispose(bool disposing)
        {

        }
        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            SynchronizeContext.Post(_ => Dispose(true), null);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}
