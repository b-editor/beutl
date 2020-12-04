using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Media;
using BEditor.Core.Properties;
using BEditor.Drawing;

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


        #region Properties

        public override string Name => Resources.ColorKey;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            MaxColor,
            MinColor
        };


        [DataMember(Order = 0)]
        public ColorProperty MaxColor { get; private set; }

        [DataMember(Order = 1)]
        public ColorProperty MinColor { get; private set; }

        #endregion


        public override void Render(EffectRenderArgs<Image<BGRA32>> args) { }

        public override void PropertyLoaded()
        {
            MaxColor.ExecuteLoaded(MaxColorMetadata);
            MinColor.ExecuteLoaded(MinColorMetadata);
        }
    }
}
