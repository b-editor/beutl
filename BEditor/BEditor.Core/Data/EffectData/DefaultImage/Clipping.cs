using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.EffectData {
    [DataContract(Namespace = "")]
    public class Clipping : ImageEffect {
        public static readonly EasePropertyMetadata TopMetadata = new EasePropertyMetadata(Resources.Top, 0, float.NaN, 0);
        public static readonly EasePropertyMetadata BottomMetadata = new EasePropertyMetadata(Resources.Bottom, 0, float.NaN, 0);
        public static readonly EasePropertyMetadata LeftMetadata = new EasePropertyMetadata(Resources.Left, 0, float.NaN, 0);
        public static readonly EasePropertyMetadata RightMetadata = new EasePropertyMetadata(Resources.Right, 0, float.NaN, 0);
        public static readonly CheckPropertyMetadata AdjustCoordinatesMetadata = new CheckPropertyMetadata(Resources.Adjust_coordinates);

        #region ImageEffect

        public override string Name => Resources.Clipping;

        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            Top,
            Bottom,
            Left,
            Right,
            AdjustCoordinates
        };

        public override void Draw(ref Image source, EffectLoadArgs args) {
            int top = (int)Top.GetValue(args.Frame);
            int bottom = (int)Bottom.GetValue(args.Frame);
            int left = (int)Left.GetValue(args.Frame);
            int right = (int)Right.GetValue(args.Frame);

            if (AdjustCoordinates.IsChecked && ClipData.Effect[0] is ImageObject image) {
                image.Coordinate.CenterX.Optional += -(right / 2) + (left / 2);
                image.Coordinate.CenterY.Optional += -(top / 2) + (bottom / 2);
            }

            source.Clip(top, bottom, left, right);
        }

        #endregion

        public Clipping() {
            Top = new EaseProperty(TopMetadata);
            Bottom = new EaseProperty(BottomMetadata);
            Left = new EaseProperty(LeftMetadata);
            Right = new EaseProperty(RightMetadata);
            AdjustCoordinates = new CheckProperty(AdjustCoordinatesMetadata);
        }


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(TopMetadata), typeof(Clipping))]
        public EaseProperty Top { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(BottomMetadata), typeof(Clipping))]
        public EaseProperty Bottom { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(LeftMetadata), typeof(Clipping))]
        public EaseProperty Left { get; set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(RightMetadata), typeof(Clipping))]
        public EaseProperty Right { get; set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(AdjustCoordinatesMetadata), typeof(Clipping))]
        public CheckProperty AdjustCoordinates { get; set; }
    }
}
