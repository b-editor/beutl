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
    /// Represents an <see cref="ImageObject"/> to draw a shape.
    /// </summary>
    [CustomClipUI(Color = 0x0091ea)]
    public sealed class Shape : ImageObject
    {
        /// <summary>
        /// Represents <see cref="Width"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata WidthMetadata = new(Strings.Width, 100, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="Height"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata HeightMetadata = new(Strings.Height, 100, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="Line"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata LineMetadata = new(Strings.LineWidth, 4000, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="Color"/> metadata.
        /// </summary>
        public static readonly ColorPropertyMetadata ColorMetadata = new(Strings.Color, Drawing.Color.Light);
        /// <summary>
        /// Represents <see cref="Type"/> metadata.
        /// </summary>
        public static readonly SelectorPropertyMetadata TypeMetadata = new(Strings.Type, new string[]
        {
            Strings.Ellipse,
            Strings.Rectangle
        });

        /// <summary>
        /// Initializes a new instance of the <see cref="Shape"/> class.
        /// </summary>
        public Shape()
        {
            Width = new(WidthMetadata);
            Height = new(HeightMetadata);
            Line = new(LineMetadata);
            Color = new(ColorMetadata);
            Type = new(TypeMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Strings.Shape;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Scale,
            Blend,
            Rotate,
            Material,
            Width,
            Height,
            Line,
            Color,
            Type
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the shape.
        /// </summary>
        [DataMember]
        public EaseProperty Width { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the height of the shape.
        /// </summary>
        [DataMember]
        public EaseProperty Height { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the line width of the shape.
        /// </summary>
        [DataMember]
        public EaseProperty Line { get; private set; }
        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the color of the shape.
        /// </summary>
        [DataMember]
        public ColorProperty Color { get; private set; }
        /// <summary>
        /// Get the <see cref="SelectorProperty"/> to select the type of the shape.
        /// </summary>
        [DataMember]
        public SelectorProperty Type { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var width = (int)Width[args.Frame];
            var height = (int)Height[args.Frame];

            if (width <= 0 || height <= 0) return new(1, 1, default(BGRA32));

            if (Type.Index == 0)
            {
                return Image.Ellipse(width, height, (int)Line[args.Frame], Color.Value);
            }
            else
            {
                return Image.Rect(width, height, (int)Line[args.Frame], Color.Value);
            }
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            Width.Load(WidthMetadata);
            Height.Load(HeightMetadata);
            Line.Load(LineMetadata);
            Color.Load(ColorMetadata);
            Type.Load(TypeMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            Width.Unload();
            Height.Unload();
            Line.Unload();
            Color.Unload();
            Type.Unload();
        }
    }
}
