using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Primitive.Properties.PrimitiveGroup
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

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z,
            CenterX,
            CenterY,
            CenterZ
        };
        [DataMember(Order = 0)]
        public EaseProperty X { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Y { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty Z { get; private set; }
        [DataMember(Order = 3)]
        public EaseProperty CenterX { get; private set; }
        [DataMember(Order = 4)]
        public EaseProperty CenterY { get; private set; }
        [DataMember(Order = 5)]
        public EaseProperty CenterZ { get; private set; }

        public override void PropertyLoaded()
        {
            X.ExecuteLoaded(XMetadata);
            Y.ExecuteLoaded(YMetadata);
            Z.ExecuteLoaded(ZMetadata);
            CenterX.ExecuteLoaded(CenterXMetadata);
            CenterY.ExecuteLoaded(CenterYMetadata);
            CenterZ.ExecuteLoaded(CenterZMetadata);
        }
        public void ResetOptional()
        {
            CenterX.Optional = 0;
            CenterY.Optional = 0;
            CenterZ.Optional = 0;
        }
    }
}
