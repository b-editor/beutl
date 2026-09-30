using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Textures;

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
        /// <remarks>Custom material resources override this member to expose their backend pipeline.</remarks>
        protected internal abstract IPipeline3D? Pipeline { get; }

        /// <summary>
        /// Gets whether this material is transparent and should use forward rendering.
        /// Default is false (opaque, deferred rendering).
        /// </summary>
        public virtual bool IsTransparent => false;

        /// <summary>Whether both sides of a surface are drawn; otherwise its back faces are culled.</summary>
        protected internal virtual bool IsDoubleSided => false;

        /// <summary>Whether the material draws nothing at all, so its surfaces cannot be seen or clicked.</summary>
        protected internal virtual bool IsInvisible => false;

        /// <summary>Whether drawing the material writes depth, so later surfaces at the same depth fail the test.</summary>
        protected internal virtual bool WritesDepth => !IsTransparent;

        /// <summary>
        /// Enumerates the texture resources that must be available while this material is rendered.
        /// </summary>
        /// <returns>The texture resources referenced by this material.</returns>
        /// <remarks>The default implementation declares no texture dependencies.</remarks>
        protected internal virtual IEnumerable<TextureSource.Resource> EnumerateTextureSources()
        {
            return [];
        }

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
