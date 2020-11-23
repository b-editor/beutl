using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Media;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    [DataContract(Namespace = "")]
    public class Border : ImageEffect
    {
        public static readonly EasePropertyMetadata SizeMetadata = new(Resources.Size, 10, float.NaN, 1);
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, 255, 255, 255);


        public Border()
        {
            Size = new(SizeMetadata);
            Color = new(ColorMetadata);
        }


        #region Properties

        public override string Name => Resources.Border;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size,
            Color
        };


        [DataMember(Order = 0)]
        public EaseProperty Size { get; private set; }

        [DataMember(Order = 1)]
        public ColorProperty Color { get; private set; }

        #endregion


        public override void Render(ref Image source, EffectRenderArgs args) => source.ToRenderable().Border((int)Size.GetValue(args.Frame), Color.Color);

        public override void PropertyLoaded()
        {
            Size.ExecuteLoaded(SizeMetadata);
            Color.ExecuteLoaded(ColorMetadata);
        }
    }
}
