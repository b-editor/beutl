using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    [DataContract]
    public class ColorKey : ImageEffect
    {
        public static readonly ColorPropertyMetadata MaxColorMetadata = new(Resources.Color, Color.Light);
        public static readonly ColorPropertyMetadata MinColorMetadata = new(Resources.Color, Color.FromARGB(100, 100, 100, 255));

        public ColorKey()
        {
            MaxColor = new(MaxColorMetadata);
            MinColor = new(MinColorMetadata);
        }

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

        public override void Render(EffectRenderArgs<Image<BGRA32>> args) { }
        public override void Loaded()
        {
            base.Loaded();
            MaxColor.ExecuteLoaded(MaxColorMetadata);
            MinColor.ExecuteLoaded(MinColorMetadata);
        }
        public override void Unloaded()
        {
            base.Unloaded();
            foreach (var pr in Children)
            {
                pr.Unloaded();
            }
        }
    }
}
