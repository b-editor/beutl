using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Materials;

/// <summary>
/// Base class for 3D materials.
/// </summary>
public abstract partial class Material3D : EngineObject
{
    public Material3D()
    {
        ScanProperties<Material3D>();
    }

    public abstract partial class Resource
    {
        /// <summary>
        /// Gets or sets whether the pipeline has been initialized.
        /// </summary>
        protected bool IsPipelineInitialized { get; set; }

        /// <summary>
        /// Gets the pipeline for this material, or null if not yet created.
        /// </summary>
        internal abstract IPipeline3D? Pipeline { get; }

        /// <summary>
        /// Gets whether this material is transparent and should use forward rendering.
        /// Default is false (opaque, deferred rendering).
        /// </summary>
        public virtual bool IsTransparent => false;

        /// <summary>
        /// Ensures the pipeline is created for this material.
        /// This method is called once before rendering.
        /// </summary>
        /// <param name="context">The 3D rendering context.</param>
        public abstract void EnsurePipeline(RenderContext3D context);

        /// <summary>
        /// Binds this material for rendering an object with an explicit world matrix.
        /// Updates uniforms and binds the pipeline and descriptor sets.
        /// </summary>
        /// <param name="context">The 3D rendering context.</param>
        /// <param name="obj">The object being rendered.</param>
        /// <param name="worldMatrix">The world matrix to use for rendering.</param>
        public abstract void Bind(RenderContext3D context, Object3D.Resource obj, Matrix4x4 worldMatrix);
    }
}
