using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using static BEditor.Primitive.Objects.Figure;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ImageObject"/> that draws a rectangle with rounded corners.
    /// </summary>
    [CustomClipUI(Color = 0x0091ea)]
    public sealed class RoundRect : ImageObject
    {
        /// <summary>
        /// Represents <see cref="Radius"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata RadiusMetadata = new("Radius", 20, Min: 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundRect"/> class.
        /// </summary>
        public RoundRect()
        {
            Width = new(WidthMetadata);
            Height = new(HeightMetadata);
            Radius = new(RadiusMetadata);
            Line = new(LineMetadata);
            Color = new(ColorMetadata);
        }

        /// <inheritdoc/>
        public override string Name => "RoundRect";
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
            Radius,
            Line,
            Color
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the shape.
        /// </summary>
        [DataMember]
        public EaseProperty Width { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the shape.
        /// </summary>
        [DataMember]
        public EaseProperty Height { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the roundness of a shape.
        /// </summary>
        [DataMember]
        public EaseProperty Radius { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the line width of the shape.
        /// </summary>
        [DataMember]
        public EaseProperty Line { get; private set; }
        /// <summary>
        /// Get the <see cref="SelectorProperty"/> to select the type of the shape.
        /// </summary>
        [DataMember]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var f = args.Frame;
            var r = (int)Radius[f];
            return Image.RoundRect((int)Width[f], (int)Height[f], (int)Line[f], r, r, Color.Value);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            Width.Load(WidthMetadata);
            Height.Load(HeightMetadata);
            Radius.Load(RadiusMetadata);
            Line.Load(LineMetadata);
            Color.Load(ColorMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            Width.Unload();
            Height.Unload();
            Radius.Unload();
            Line.Unload();
            Color.Unload();
        }
    }
}
