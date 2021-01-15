using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Properties;

namespace BEditor.Core.Data.Property
{
    //Memo : xml英語ここまで
    /// <summary>
    /// Represents the property used by <see cref="EffectElement"/>.
    /// </summary>
    [DataContract]
    public abstract class PropertyElement : ComponentObject, IChild<EffectElement>, IPropertyElement, IHasId, IHasName
    {
        private static readonly PropertyChangedEventArgs metadataArgs = new(nameof(PropertyMetadata));
        private PropertyElementMetadata propertyMetadata;
        private int? id;


        /// <summary>
        /// このプロパティの親要素を取得します
        /// </summary>
        public virtual EffectElement Parent { get; set; }
        /// <summary>
        /// プロパティのメタデータを取得または設定します
        /// </summary>
        public PropertyElementMetadata PropertyMetadata
        {
            get => propertyMetadata;
            set => SetValue(value, ref propertyMetadata, metadataArgs);
        }
        /// <inheritdoc/>
        public int Id => id ??= Parent.Children.ToList().IndexOf(this);
        /// <inheritdoc/>
        public string Name => propertyMetadata?.Name ?? Id.ToString();
        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }

        /// <inheritdoc/>
        public virtual void Loaded()
        {
            if (IsLoaded) return;

            IsLoaded = true;
        }


        /// <inheritdoc/>
        public override string ToString() => $"(Name:{PropertyMetadata?.Name})";
        /// <inheritdoc/>
        public virtual void Unloaded()
        {
            if (!IsLoaded) return;

            IsLoaded = false;
        }
    }

    [DataContract]
    public abstract class PropertyElement<T> : PropertyElement where T : PropertyElementMetadata
    {
        public new T PropertyMetadata
        {
            get => base.PropertyMetadata as T;
            set => base.PropertyMetadata = value;
        }
    }

    /// <summary>
    /// <see cref="BEditor.Core.Data.Property.PropertyElement"/> のメタデータを表します
    /// </summary>
    public record PropertyElementMetadata(string Name);
}
