using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Property.PrimitiveGroup
{
    [DataContract]
    public sealed class Angle : ExpandGroup
    {
        public static readonly EasePropertyMetadata AngleXMetadata = new(Resources.AngleX);
        public static readonly EasePropertyMetadata AngleYMetadata = new(Resources.AngleY);
        public static readonly EasePropertyMetadata AngleZMetadata = new(Resources.AngleZ);

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            AngleX,
            AngleY,
            AngleZ
        };
        [DataMember(Order = 0)]
        public EaseProperty AngleX { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty AngleY { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty AngleZ { get; private set; }

        public Angle(PropertyElementMetadata metadata) : base(metadata)
        {
            AngleX = new(AngleXMetadata);
            AngleY = new(AngleYMetadata);
            AngleZ = new(AngleZMetadata);
        }

        protected override void OnLoad()
        {
            AngleX.Load(AngleXMetadata);
            AngleY.Load(AngleYMetadata);
            AngleZ.Load(AngleZMetadata);
        }
        protected override void OnUnload()
        {
            AngleX.Unload();
            AngleY.Unload();
            AngleZ.Unload();
        }
    }
}
