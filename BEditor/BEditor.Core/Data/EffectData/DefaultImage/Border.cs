using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Extensions;
using BEditor.Core.Media;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.EffectData
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

        public override void Render(ref Image source, EffectRenderArgs args)
        {
            source.Border((int)Size.GetValue(args.Frame), Color);
        }

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size,
            Color
        };

        public override void PropertyLoaded()
        {
            Size.ExecuteLoaded(SizeMetadata);
            Color.ExecuteLoaded(ColorMetadata);
        }

        #endregion


        [DataMember(Order = 0)]
        public EaseProperty Size { get; private set; }

        [DataMember(Order = 1)]
        public ColorProperty Color { get;private set; }
    }
}
