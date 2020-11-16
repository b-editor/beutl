using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Data.PropertyData;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.PropertyData.Default
{
    [DataContract(Namespace = "")]
    public sealed class Material : ExpandGroup
    {
        public static readonly ColorAnimationPropertyMetadata AmbientMetadata = new(Resources.Ambient, 255, 255, 255, 255, true);
        public static readonly ColorAnimationPropertyMetadata DiffuseMetadata = new(Resources.Diffuse, 255, 255, 255, 255, true);
        public static readonly ColorAnimationPropertyMetadata SpecularMetadata = new(Resources.Specular, 255, 255, 255, 255, true);
        public static readonly EasePropertyMetadata ShininessMetadata = new(Resources.Shininess, 10, float.NaN, 1);


        public Material(PropertyElementMetadata constant) : base(constant)
        {
            Ambient = new(AmbientMetadata);
            Diffuse = new(DiffuseMetadata);
            Specular = new(SpecularMetadata);
            Shininess = new(ShininessMetadata);
        }

        #region ExpandGroup

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Ambient,
            Diffuse,
            Specular,
            Shininess
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(AmbientMetadata), typeof(Material))]
        public ColorAnimationProperty Ambient { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(DiffuseMetadata), typeof(Material))]
        public ColorAnimationProperty Diffuse { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(SpecularMetadata), typeof(Material))]
        public ColorAnimationProperty Specular { get; private set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(ShininessMetadata), typeof(Material))]
        public EaseProperty Shininess { get; private set; }
    }
}
