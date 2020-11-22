using System;
using System.Collections.Generic;
using System.Text;

namespace BEditor.Core.Data.Property.EasingProperty
{
    /// <summary>
    /// <see cref="EasingFunc"/> で利用可能なプロパティを表します
    /// </summary>
    public interface IEasingProperty : IPropertyElement
    {
        public new EffectElement Parent { get; set; }
    }
}
