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
    public class Monoc : ImageEffect
    {
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, 255, 255, 255);

        public Monoc()
        {
            Color = new(ColorMetadata);
        }


        #region ImageEffect
        public override string Name => Resources.Monoc;

        public override void Render(ref Image source, EffectRenderArgs args) => source.ToRenderable().SetColor(Color.Value);

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Color
        };

        public override void PropertyLoaded()
        {
            Color.ExecuteLoaded(ColorMetadata);
        }

        #endregion



        [DataMember(Order = 0)]
        public ColorProperty Color { get; private set; }
    }
}
