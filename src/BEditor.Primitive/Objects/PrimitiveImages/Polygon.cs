using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using static BEditor.Primitive.Objects.Figure;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Get an <see cref="ImageObject"/> to draw a polygon.
    /// </summary>
    [CustomClipUI(Color = 0x0091ea)]
    public sealed class Polygon : ImageObject
    {
        /// <summary>
        /// Represents <see cref="Number"/> metadata.
        /// </summary>
        public static readonly ValuePropertyMetadata NumberMetadata = new("角", 3, Min: 3);

        /// <summary>
        /// Iniitializes a new instance of the <see cref="Polygon"/> class.
        /// </summary>
        public Polygon()
        {
            Width = new(WidthMetadata);
            Height = new(HeightMetadata);
            Number = new(NumberMetadata);
            Color = new(ColorMetadata);
        }

        /// <inheritdoc/>
        public override string Name => "Polygon";
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Width,
            Height,
            Number,
            Color
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the polygon.
        /// </summary>
        [DataMember]
        public EaseProperty Width { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the height of the polygon.
        /// </summary>
        [DataMember]
        public EaseProperty Height { get; private set; }
        /// <summary>
        /// Gets the <see cref="ValueProperty"/> representing the number of corners of a polygon.
        /// </summary>
        [DataMember]
        public ValueProperty Number { get; private set; }
        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the color of the polygon.
        /// </summary>
        [DataMember]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var width = (int)Width[args.Frame];
            var height = (int)Height[args.Frame];

            if (width <= 0 || height <= 0) return new(1, 1, default(BGRA32));

            return Image.Polygon((int)Number.Value, width, height, Color.Value);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            Width.Load(WidthMetadata);
            Height.Load(HeightMetadata);
            Number.Load(NumberMetadata);
            Color.Load(ColorMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            Width.Unload();
            Height.Unload();
            Number.Unload();
            Color.Unload();
        }
    }
}
