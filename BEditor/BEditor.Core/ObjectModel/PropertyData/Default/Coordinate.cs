using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.ObjectModel.PropertyData;
using BEditor.Properties;

namespace BEditor.ObjectModel.PropertyData.Default
{
    [DataContract(Namespace = "")]
    public sealed class Coordinate : ExpandGroup
    {
        public static readonly EasePropertyMetadata XMetadata = new(Resources.X, 0);
        public static readonly EasePropertyMetadata YMetadata = new(Resources.Y, 0);
        public static readonly EasePropertyMetadata ZMetadata = new(Resources.Z, 0);
        public static readonly EasePropertyMetadata CenterXMetadata = new(Resources.CenterX, 0, float.NaN, float.NaN, true);
        public static readonly EasePropertyMetadata CenterYMetadata = new(Resources.CenterY, 0, float.NaN, float.NaN, true);
        public static readonly EasePropertyMetadata CenterZMetadata = new(Resources.CenterZ, 0, float.NaN, float.NaN, true);

        public Coordinate(PropertyElementMetadata constant) : base(constant)
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
            CenterX = new(CenterXMetadata);
            CenterY = new(CenterYMetadata);
            CenterZ = new(CenterZMetadata);
        }


        #region ExpandGroup
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z,
            CenterX,
            CenterY,
            CenterZ
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(XMetadata), typeof(Coordinate))]
        public EaseProperty X { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(YMetadata), typeof(Coordinate))]
        public EaseProperty Y { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(ZMetadata), typeof(Coordinate))]
        public EaseProperty Z { get; private set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(CenterXMetadata), typeof(Coordinate))]
        public EaseProperty CenterX { get; private set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(CenterYMetadata), typeof(Coordinate))]
        public EaseProperty CenterY { get; private set; }

        [DataMember(Order = 5)]
        [PropertyMetadata(nameof(CenterZMetadata), typeof(Coordinate))]
        public EaseProperty CenterZ { get; private set; }

        public void ResetOptional()
        {
            CenterX.Optional = 0;
            CenterY.Optional = 0;
            CenterZ.Optional = 0;
        }
    }
}
