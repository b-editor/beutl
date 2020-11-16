using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.Media;
using BEditor.Properties;

namespace BEditor.ObjectModel.EffectData
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


        #region ImageEffect
        public override string Name => Resources.Blur;

        #region Draw
        public override void Render(ref Image source, EffectRenderArgs args)
        {
            if (Mode.Index == 0)
            {
                source.Blur((int)Size.GetValue(args.Frame), AlphaBlur.IsChecked);
            }
            else if (Mode.Index == 1)
            {
                source.GaussianBlur((int)Size.GetValue(args.Frame), AlphaBlur.IsChecked);
            }
            else
            {
                source.MedianBlur((int)Size.GetValue(args.Frame), AlphaBlur.IsChecked);
            }
        }
        #endregion

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size,
            AlphaBlur,
            Mode
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(SizeMetadata), typeof(Blur))]
        public EaseProperty Size { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(AlphaBlurMetadata), typeof(Blur))]
        public CheckProperty AlphaBlur { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(ModeMetadata), typeof(Blur))]
        public SelectorProperty Mode { get; private set; }
    }
}
