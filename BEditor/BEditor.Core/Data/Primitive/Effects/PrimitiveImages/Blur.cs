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
    public class Blur : ImageEffect
    {
        public static readonly EasePropertyMetadata SizeMetadata = new(Resources.Size, 70, float.NaN, 0);
        public static readonly CheckPropertyMetadata AlphaBlurMetadata = new(Resources.Diffusion, false);
        public static readonly SelectorPropertyMetadata ModeMetadata = new(Resources.BlurMode, new string[3]
        {
            Resources.Standard,
            Resources.Gauss,
            Resources.Median
        });


        public Blur()
        {
            Size = new(SizeMetadata);
            AlphaBlur = new(AlphaBlurMetadata);
            Mode = new(ModeMetadata);
        }


        #region Properties

        public override string Name => Resources.Blur;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size,
            AlphaBlur,
            Mode
        };


        [DataMember(Order = 0)]
        public EaseProperty Size { get; private set; }

        [DataMember(Order = 1)]
        public CheckProperty AlphaBlur { get; private set; }

        [DataMember(Order = 2)]
        public SelectorProperty Mode { get; private set; }

        #endregion


        public override void Render(ref Image source, EffectRenderArgs args)
        {
            if (Mode.Index == 0)
            {
                source.ToRenderable().Blur((int)Size.GetValue(args.Frame), AlphaBlur.IsChecked);
            }
            else if (Mode.Index == 1)
            {
                source.ToRenderable().GaussianBlur((int)Size.GetValue(args.Frame), AlphaBlur.IsChecked);
            }
            else
            {
                source.ToRenderable().MedianBlur((int)Size.GetValue(args.Frame), AlphaBlur.IsChecked);
            }
        }

        public override void PropertyLoaded()
        {
            Size.ExecuteLoaded(SizeMetadata);
            AlphaBlur.ExecuteLoaded(AlphaBlurMetadata);
            Mode.ExecuteLoaded(ModeMetadata);
        }

    }
}
