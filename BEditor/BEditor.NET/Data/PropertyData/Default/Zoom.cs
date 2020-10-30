using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.NET.Data.PropertyData;

namespace BEditor.NET.Data.PropertyData.Default {
    [DataContract(Namespace = "")]
    public class Zoom : ExpandGroup {
        public static readonly EasePropertyMetadata ZoomMetadata = new EasePropertyMetadata(Properties.Resources.Zoom, 100);
        public static readonly EasePropertyMetadata ScaleXMetadata = new EasePropertyMetadata(Properties.Resources.X, 100);
        public static readonly EasePropertyMetadata ScaleYMetadata = new EasePropertyMetadata(Properties.Resources.Y, 100);
        public static readonly EasePropertyMetadata ScaleZMetadata = new EasePropertyMetadata(Properties.Resources.Z, 100);


        public Zoom(PropertyElementMetadata constant) : base(constant) {
            Scale = new EaseProperty(ZoomMetadata);
            ScaleX = new EaseProperty(ScaleXMetadata);
            ScaleY = new EaseProperty(ScaleYMetadata);
            ScaleZ = new EaseProperty(ScaleZMetadata);
        }


        #region ExpandGroup
        public override IList<PropertyElement> GroupItems => new List<PropertyElement>() {
            Scale,
            ScaleX,
            ScaleY,
            ScaleZ
        };

        #endregion


        [DataMember(Name = "Zoom", Order = 0)]
        [PropertyMetadata(nameof(ZoomMetadata), typeof(Zoom))]
        public EaseProperty Scale { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(ScaleXMetadata), typeof(Zoom))]
        public EaseProperty ScaleX { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(ScaleYMetadata), typeof(Zoom))]
        public EaseProperty ScaleY { get; set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(ScaleZMetadata), typeof(Zoom))]
        public EaseProperty ScaleZ { get; set; }
    }
}
