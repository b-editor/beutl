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
        [PropertyMetadata(nameof(MaxColorMetadata), typeof(ColorKey))]
        public ColorProperty MaxColor { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(MinColorMetadata), typeof(ColorKey))]
        public ColorProperty MinColor { get; private set; }
    }
}
