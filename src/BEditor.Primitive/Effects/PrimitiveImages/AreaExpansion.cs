using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that expands the area of an image.
    /// </summary>
    public sealed class AreaExpansion : ImageEffect
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AreaExpansion"/> class.
        /// </summary>
        public AreaExpansion()
        {
            Top = new(Clipping.TopMetadata);
            Bottom = new(Clipping.BottomMetadata);
            Left = new(Clipping.LeftMetadata);
            Right = new(Clipping.RightMetadata);
            AdjustCoordinates = new(Clipping.AdjustCoordinatesMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Strings.AreaExpansion;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Top,
            Bottom,
            Left,
            Right,
            AdjustCoordinates
        };
        /// <summary>
        /// Get an <see cref="EaseProperty"/> that represents the number of pixels to add
        /// </summary>
        [DataMember]
        public EaseProperty Top { get; private set; }
        /// <summary>
        /// Get an <see cref="EaseProperty"/> that represents the number of pixels to add
        /// </summary>
        [DataMember]
        public EaseProperty Bottom { get; private set; }
        /// <summary>
        /// Get an <see cref="EaseProperty"/> that represents the number of pixels to add
        /// </summary>
        [DataMember]
        public EaseProperty Left { get; private set; }
        /// <summary>
        /// Get an <see cref="EaseProperty"/> that represents the number of pixels to add
        /// </summary>
        [DataMember]
        public EaseProperty Right { get; private set; }
        /// <summary>
        /// Get the <see cref="CheckProperty"/> to adjust the coordinates.
        /// </summary>
        [DataMember]
        public CheckProperty AdjustCoordinates { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            int top = (int)Top.GetValue(args.Frame);
            int bottom = (int)Bottom.GetValue(args.Frame);
            int left = (int)Left.GetValue(args.Frame);
            int right = (int)Right.GetValue(args.Frame);

            if (AdjustCoordinates.Value && Parent!.Effect[0] is ImageObject image)
            {
                image.Coordinate.CenterX.Optional = (right / 2) - (left / 2);
                image.Coordinate.CenterY.Optional = (top / 2) - (bottom / 2);
            }

            var img = args.Value.MakeBorder(top, bottom, left, right);

            args.Value.Dispose();

            args.Value = img;
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Top.Load(Clipping.TopMetadata);
            Bottom.Load(Clipping.BottomMetadata);
            Left.Load(Clipping.LeftMetadata);
            Right.Load(Clipping.RightMetadata);
            AdjustCoordinates.Load(Clipping.AdjustCoordinatesMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            foreach (var prop in Children)
            {
                prop.Unload();
            }
        }
    }
}