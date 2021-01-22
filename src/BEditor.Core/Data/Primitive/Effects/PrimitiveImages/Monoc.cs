using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class Monoc : ImageEffect
    {
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, Drawing.Color.Light);

        public Monoc()
        {
            Color = new(ColorMetadata);
        }

        public override string Name => Resources.Monoc;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Color
        };
        [DataMember(Order = 0)]
        public ColorProperty Color { get; private set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.SetColor(Color.Color);
        }

        protected override void OnLoad()
        {
            Color.Load(ColorMetadata);
        }
        protected override void OnUnload()
        {
            foreach (var pr in Children)
            {
                pr.Unload();
            }
        }
    }
}
