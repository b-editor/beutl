using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.EffectData;

namespace BEditor.Core.Data.PropertyData.Generic
{
    public abstract class PropertyElement<T> : PropertyElement, IChild<EffectElement>, IExtensibleDataObject, INotifyPropertyChanged
        where T : PropertyElementMetadata
    {
        public new T PropertyMetadata
        {
            get => (T)base.PropertyMetadata;
            set => base.PropertyMetadata = value;
        }
    }
}
