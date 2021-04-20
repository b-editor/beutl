using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// 
    /// </summary>
    public sealed class Framebuffer : ImageObject
    {
        /// <summary>
        /// Defines the <see cref="BufferClear"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Framebuffer, CheckProperty> BufferClearProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, Framebuffer>(
            nameof(BufferClear),
            owner => owner.BufferClear,
            (owner, obj) => owner.BufferClear = obj,
            new CheckPropertyMetadata(Strings.ClearFramebuffer));

        /// <summary>
        /// Initializes a new instance of the <see cref="Framebuffer"/> class.
        /// </summary>
#pragma warning disable CS8618
        public Framebuffer()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Framebuffer;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return Coordinate;
                yield return Scale;
                yield return Blend;
                yield return Rotate;
                yield return Material;
                yield return BufferClear;
            }
        }

        /// <summary>
        /// Gets the <see cref="CheckProperty"/> representing the value whether to clear the frame buffer.
        /// </summary>
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
    }
}