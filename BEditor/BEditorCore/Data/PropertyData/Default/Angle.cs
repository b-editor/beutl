using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditorCore.Data.PropertyData;

namespace BEditorCore.Data.PropertyData.Default {
    [DataContract(Namespace = "")]
    public class Angle : ExpandGroup {
        public static readonly EasePropertyMetadata AngleXMetadata = new EasePropertyMetadata(Properties.Resources.AngleX);
        public static readonly EasePropertyMetadata AngleYMetadata = new EasePropertyMetadata(Properties.Resources.AngleY);
        public static readonly EasePropertyMetadata AngleZMetadata = new EasePropertyMetadata(Properties.Resources.AngleZ);

        public Angle(PropertyElementMetadata constant) : base(constant) {
            AngleX = new EaseProperty(AngleXMetadata);
            AngleY = new EaseProperty(AngleYMetadata);
            AngleZ = new EaseProperty(AngleZMetadata);
        }


        #region ExpandGroup

        public override IList<PropertyElement> GroupItems => new List<PropertyElement>() {
            AngleX,
            AngleY,
            AngleZ
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata("AngleXMetadata", typeof(Angle))]
        public EaseProperty AngleX { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata("AngleYMetadata", typeof(Angle))]
        public EaseProperty AngleY { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata("AngleZMetadata", typeof(Angle))]
        public EaseProperty AngleZ { get; set; }
    }
}
