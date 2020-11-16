using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.ObjectModel.PropertyData;
using BEditor.Properties;

namespace BEditor.ObjectModel.PropertyData.Default
{
    [DataContract(Namespace = "")]
    public sealed class Zoom : ExpandGroup
    {
        public static readonly EasePropertyMetadata ZoomMetadata = new(Resources.Zoom, 100);
        public static readonly EasePropertyMetadata ScaleXMetadata = new(Resources.X, 100);
        public static readonly EasePropertyMetadata ScaleYMetadata = new(Resources.Y, 100);
        public static readonly EasePropertyMetadata ScaleZMetadata = new(Resources.Z, 100);


        public Zoom(PropertyElementMetadata constant) : base(constant)
        {
            Scale = new(ZoomMetadata);
            ScaleX = new(ScaleXMetadata);
            ScaleY = new(ScaleYMetadata);
            ScaleZ = new(ScaleZMetadata);
        }


        #region ExpandGroup
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Scale,
            ScaleX,
            ScaleY,
            ScaleZ
        };

        #endregion


        [DataMember(Name = "Zoom", Order = 0)]
        [PropertyMetadata(nameof(ZoomMetadata), typeof(Zoom))]
        public EaseProperty Scale { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(ScaleXMetadata), typeof(Zoom))]
        public EaseProperty ScaleX { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(ScaleYMetadata), typeof(Zoom))]
        public EaseProperty ScaleY { get; private set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(ScaleZMetadata), typeof(Zoom))]
        public EaseProperty ScaleZ { get; private set; }
    }
}
