using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Properties;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class Framebuffer : ImageObject
    {
        /// <summary>
        /// Represents <see cref="BufferClear"/> metadata.
        /// </summary>
        public static readonly CheckPropertyMetadata BufferClearMetadata = new(Resources.ClearFramebuffer);

        /// <summary>
        /// Initializes a new instance of the <see cref="Framebuffer"/> class.
        /// </summary>
        public Framebuffer()
        {
            BufferClear = new(BufferClearMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.Framebuffer;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            BufferClear
        };
        /// <summary>
        /// Gets the <see cref="CheckProperty"/> representing the value whether to clear the frame buffer.
        /// </summary>
        [DataMember]
        public CheckProperty BufferClear { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectRenderArgs args)
        {
            var scene = Parent.Parent;
            var image = new Image<BGRA32>(scene.Width, scene.Height);

            scene.GraphicsContext?.ReadImage(image);

            if (BufferClear.Value)
            {
                scene.GraphicsContext?.Clear();
            }

            return image;
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            BufferClear.Load(BufferClearMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            BufferClear.Unload();
        }
    }
}
