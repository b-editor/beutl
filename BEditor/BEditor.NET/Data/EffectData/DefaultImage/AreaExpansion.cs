using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditor.NET.Data.ObjectData;
using BEditor.NET.Data.ProjectData;
using BEditor.NET.Data.PropertyData;
using BEditor.NET.Media;
using BEditor.NET.Properties;

namespace BEditor.NET.Data.EffectData {
    [DataContract(Namespace = "")]
    public class AreaExpansion : ImageEffect {

        #region ImageEffect

        public override string Name => Resources.AreaExpansion;

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
                image.Coordinate.CenterX.Optional = (right / 2) - (left / 2);
                image.Coordinate.CenterY.Optional = (top / 2) - (bottom / 2);
            }

            source.AreaExpansion(top, bottom, left, right);
        }

        #endregion

        public AreaExpansion() {
            Top = new EaseProperty(Clipping.TopMetadata);
            Bottom = new EaseProperty(Clipping.BottomMetadata);
            Left = new EaseProperty(Clipping.LeftMetadata);
            Right = new EaseProperty(Clipping.RightMetadata);
            AdjustCoordinates = new CheckProperty(Clipping.AdjustCoordinatesMetadata);
        }


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(Clipping.TopMetadata), typeof(Clipping))]
        public EaseProperty Top { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(Clipping.BottomMetadata), typeof(Clipping))]
        public EaseProperty Bottom { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(Clipping.LeftMetadata), typeof(Clipping))]
        public EaseProperty Left { get; set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(Clipping.RightMetadata), typeof(Clipping))]
        public EaseProperty Right { get; set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(Clipping.AdjustCoordinatesMetadata), typeof(Clipping))]
        public CheckProperty AdjustCoordinates { get; set; }

    }
}
