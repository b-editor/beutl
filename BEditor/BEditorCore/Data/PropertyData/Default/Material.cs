using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditorCore.Data.PropertyData;

namespace BEditorCore.Data.PropertyData.Default {
    [DataContract(Namespace = "")]
    public class Material : ExpandGroup {
        public static readonly ColorAnimationPropertyMetadata AmbientMetadata = new ColorAnimationPropertyMetadata(Properties.Resources.Ambient, 255, 255, 255, 255, true);
        public static readonly ColorAnimationPropertyMetadata DiffuseMetadata = new ColorAnimationPropertyMetadata(Properties.Resources.Diffuse, 255, 255, 255, 255, true);
        public static readonly ColorAnimationPropertyMetadata SpecularMetadata = new ColorAnimationPropertyMetadata(Properties.Resources.Specular, 255, 255, 255, 255, true);
        public static readonly EasePropertyMetadata ShininessMetadata = new EasePropertyMetadata(Properties.Resources.Shininess, 10, float.NaN, 1);


        public Material(PropertyElementMetadata constant) : base(constant) {
            Ambient = new ColorAnimationProperty(AmbientMetadata);
            Diffuse = new ColorAnimationProperty(DiffuseMetadata);
            Specular = new ColorAnimationProperty(SpecularMetadata);
            Shininess = new EaseProperty(ShininessMetadata);
        }

        #region ExpandGroup

        public override IList<PropertyElement> GroupItems => new List<PropertyElement> {
            Ambient,
            Diffuse,
            Specular,
            Shininess
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(AmbientMetadata), typeof(Material))]
        public ColorAnimationProperty Ambient { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(DiffuseMetadata), typeof(Material))]
        public ColorAnimationProperty Diffuse { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(SpecularMetadata), typeof(Material))]
        public ColorAnimationProperty Specular { get; set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(ShininessMetadata), typeof(Material))]
        public EaseProperty Shininess { get; set; }
    }
}
