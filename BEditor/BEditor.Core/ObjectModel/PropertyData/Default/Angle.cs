using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.ObjectModel.PropertyData;
using BEditor.Properties;

namespace BEditor.ObjectModel.PropertyData.Default
{
    [DataContract(Namespace = "")]
    public sealed class Angle : ExpandGroup
    {
        public static readonly EasePropertyMetadata AngleXMetadata = new(Resources.AngleX);
        public static readonly EasePropertyMetadata AngleYMetadata = new(Resources.AngleY);
        public static readonly EasePropertyMetadata AngleZMetadata = new(Resources.AngleZ);

        public Angle(PropertyElementMetadata constant) : base(constant)
        {
            AngleX = new(AngleXMetadata);
            AngleY = new(AngleYMetadata);
            AngleZ = new(AngleZMetadata);
        }


        #region ExpandGroup

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            AngleX,
            AngleY,
            AngleZ
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(AngleXMetadata), typeof(Angle))]
        public EaseProperty AngleX { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(AngleYMetadata), typeof(Angle))]
        public EaseProperty AngleY { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(AngleZMetadata), typeof(Angle))]
        public EaseProperty AngleZ { get; private set; }
    }
}
