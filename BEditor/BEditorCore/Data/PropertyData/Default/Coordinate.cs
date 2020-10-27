using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditorCore.Data.PropertyData;

namespace BEditorCore.Data.PropertyData.Default {
    [DataContract(Namespace = "")]
    public class Coordinate : ExpandGroup {
        public static readonly EasePropertyMetadata XMetadata = new EasePropertyMetadata(Properties.Resources.X, 0);
        public static readonly EasePropertyMetadata YMetadata = new EasePropertyMetadata(Properties.Resources.Y, 0);
        public static readonly EasePropertyMetadata ZMetadata = new EasePropertyMetadata(Properties.Resources.Z, 0);
        public static readonly EasePropertyMetadata CenterXMetadata = new EasePropertyMetadata(Properties.Resources.CenterX, 0, float.NaN, float.NaN, true);
        public static readonly EasePropertyMetadata CenterYMetadata = new EasePropertyMetadata(Properties.Resources.CenterY, 0, float.NaN, float.NaN, true);
        public static readonly EasePropertyMetadata CenterZMetadata = new EasePropertyMetadata(Properties.Resources.CenterZ, 0, float.NaN, float.NaN, true);

        public Coordinate(PropertyElementMetadata constant) : base(constant) {
            X = new EaseProperty(XMetadata);
            Y = new EaseProperty(YMetadata);
            Z = new EaseProperty(ZMetadata);
            CenterX = new EaseProperty(CenterXMetadata);
            CenterY = new EaseProperty(CenterYMetadata);
            CenterZ = new EaseProperty(CenterZMetadata);
        }


        #region ExpandGroup
        public override IList<PropertyElement> GroupItems => new List<PropertyElement>() {
            X,
            Y,
            Z,
            CenterX,
            CenterY,
            CenterZ
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata("XMetadata", typeof(Coordinate))]
        public EaseProperty X { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata("YMetadata", typeof(Coordinate))]
        public EaseProperty Y { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata("ZMetadata", typeof(Coordinate))]
        public EaseProperty Z { get; set; }

        [DataMember(Order = 3)]
        [PropertyMetadata("CenterXMetadata", typeof(Coordinate))]
        public EaseProperty CenterX { get; set; }

        [DataMember(Order = 4)]
        [PropertyMetadata("CenterYMetadata", typeof(Coordinate))]
        public EaseProperty CenterY { get; set; }

        [DataMember(Order = 5)]
        [PropertyMetadata("CenterZMetadata", typeof(Coordinate))]
        public EaseProperty CenterZ { get; set; }

        public void ResetOptional() {
            CenterX.Optional = 0;
            CenterY.Optional = 0;
            CenterZ.Optional = 0;
        }
    }
}
