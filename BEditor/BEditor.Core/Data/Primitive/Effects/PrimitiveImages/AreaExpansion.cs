using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Property;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class AreaExpansion : ImageEffect
    {
        public AreaExpansion()
        {
            Top = new(Clipping.TopMetadata);
            Bottom = new(Clipping.BottomMetadata);
            Left = new(Clipping.LeftMetadata);
            Right = new(Clipping.RightMetadata);
            AdjustCoordinates = new(Clipping.AdjustCoordinatesMetadata);
        }

        public override string Name => Resources.AreaExpansion;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Top,
            Bottom,
            Left,
            Right,
            AdjustCoordinates
        };
        [DataMember(Order = 0)]
        public EaseProperty Top { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Bottom { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty Left { get; private set; }
        [DataMember(Order = 3)]
        public EaseProperty Right { get; private set; }
        [DataMember(Order = 4)]
        public CheckProperty AdjustCoordinates { get; private set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            int top = (int)Top.GetValue(args.Frame);
            int bottom = (int)Bottom.GetValue(args.Frame);
            int left = (int)Left.GetValue(args.Frame);
            int right = (int)Right.GetValue(args.Frame);

            if (AdjustCoordinates.IsChecked && Parent.Effect[0] is ImageObject image)
            {
                image.Coordinate.CenterX.Optional = (right / 2) - (left / 2);
                image.Coordinate.CenterY.Optional = (top / 2) - (bottom / 2);
            }

            var img = args.Value.MakeBorder(top, bottom, left, right);

            args.Value.Dispose();

            args.Value = img;
        }
        protected override void OnLoad()
        {
            Top.Load(Clipping.TopMetadata);
            Bottom.Load(Clipping.BottomMetadata);
            Left.Load(Clipping.LeftMetadata);
            Right.Load(Clipping.RightMetadata);
            AdjustCoordinates.Load(Clipping.AdjustCoordinatesMetadata);
        }
        protected override void OnUnload()
        {
            foreach (var prop in Children)
            {
                prop.Unload();
            }
        }
    }
}
