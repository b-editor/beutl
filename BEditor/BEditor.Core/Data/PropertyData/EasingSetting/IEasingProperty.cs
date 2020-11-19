using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;

namespace BEditor.Core.Data.PropertyData.EasingSetting
{
    /// <summary>
    /// <see cref="EasingFunc"/> で利用可能なプロパティを表します
    /// </summary>
    public interface IEasingProperty : IPropertyElement
    {
        public new EffectElement Parent { get; set; }
    }
}
