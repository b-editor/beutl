using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Drawing;

namespace BEditor.Core.Data.Primitive.Properties.PrimitiveGroup
{
    [DataContract]
    public sealed class Material : ExpandGroup
    {
        public static readonly ColorAnimationPropertyMetadata AmbientMetadata = new(Resources.Ambient, Color.Light, true);
        public static readonly ColorAnimationPropertyMetadata DiffuseMetadata = new(Resources.Diffuse, Color.Light, true);
        public static readonly ColorAnimationPropertyMetadata SpecularMetadata = new(Resources.Specular, Color.Light, true);
        public static readonly EasePropertyMetadata ShininessMetadata = new(Resources.Shininess, 10, float.NaN, 1);

        public Material(PropertyElementMetadata metadata) : base(metadata)
        {
            Ambient = new(AmbientMetadata);
            Diffuse = new(DiffuseMetadata);
            Specular = new(SpecularMetadata);
            Shininess = new(ShininessMetadata);
        }

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Ambient,
            Diffuse,
            Specular,
            Shininess
        };
        [DataMember(Order = 0)]
        public ColorAnimationProperty Ambient { get; private set; }
        [DataMember(Order = 1)]
        public ColorAnimationProperty Diffuse { get; private set; }
        [DataMember(Order = 2)]
        public ColorAnimationProperty Specular { get; private set; }
        [DataMember(Order = 3)]
        public EaseProperty Shininess { get; private set; }

        public override void Loaded()
        {
            base.Loaded();
            Ambient.ExecuteLoaded(AmbientMetadata);
            Diffuse.ExecuteLoaded(DiffuseMetadata);
            Specular.ExecuteLoaded(SpecularMetadata);
            Shininess.ExecuteLoaded(ShininessMetadata);
        }
        public override void Unloaded()
        {
            base.Unloaded();
            Ambient.Unloaded();
            Diffuse.Unloaded();
            Specular.Unloaded();
            Shininess.Unloaded();
        }
    }
}
