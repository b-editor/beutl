using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.ObjectModel.ObjectData;
using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.Media;
using BEditor.Properties;

namespace BEditor.ObjectModel.EffectData
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


        #region EffectProperty
        public override string Name => Resources.Border;

        public override void Render(ref Image source, EffectRenderArgs args) => source.Border((int)Size.GetValue(args.Frame), Color);


        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size,
            Color
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(SizeMetadata), typeof(Border))]
        public EaseProperty Size { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(ColorMetadata), typeof(Border))]
        public ColorProperty Color { get;private set; }
    }
}
