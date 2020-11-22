using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Media;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    [DataContract(Namespace = "")]
    public class ColorKey : ImageEffect
    {
        public static readonly ColorPropertyMetadata MaxColorMetadata = new(Resources.Color, 255, 255, 255);
        public static readonly ColorPropertyMetadata MinColorMetadata = new(Resources.Color, 100, 100, 100);

        public ColorKey()
        {
            MaxColor = new(MaxColorMetadata);
            MinColor = new(MinColorMetadata);
        }


        #region ImageEffect
        public override string Name => Resources.ColorKey;

        public override void Render(ref Image source, EffectRenderArgs args) { }

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            MaxColor,
            MinColor
        };

        public override void PropertyLoaded()
        {
            MaxColor.ExecuteLoaded(MaxColorMetadata);
            MinColor.ExecuteLoaded(MinColorMetadata);
        }

        #endregion


        [DataMember(Order = 0)]
        public ColorProperty MaxColor { get; private set; }

        [DataMember(Order = 1)]
        public ColorProperty MinColor { get; private set; }
    }
}
