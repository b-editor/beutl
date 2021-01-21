using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Property
{
    //Memo : xml英語ここまで
    /// <summary>
    /// Represents the property used by <see cref="EffectElement"/>.
    /// </summary>
    [DataContract]
    public class PropertyElement : ComponentObject, IChild<EffectElement>, IPropertyElement, IHasId, IHasName
    {
        private static readonly PropertyChangedEventArgs _MetadataArgs = new(nameof(PropertyMetadata));
        private PropertyElementMetadata? _PropertyMetadata;
        private int? id;


        /// <summary>
        /// このプロパティの親要素を取得します
        /// </summary>
        public virtual EffectElement? Parent { get; set; }
        /// <summary>
        /// プロパティのメタデータを取得または設定します
        /// </summary>
        public PropertyElementMetadata? PropertyMetadata
        {
            get => _PropertyMetadata;
            set => SetValue(value, ref _PropertyMetadata, _MetadataArgs);
        }
        /// <inheritdoc/>
        public int Id => (id ??= Parent?.Children?.ToList()?.IndexOf(this)) ?? -1;
        /// <inheritdoc/>
        public string Name => _PropertyMetadata?.Name ?? Id.ToString();
        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }


        /// <inheritdoc/>
        public override string ToString() => $"(Name:{PropertyMetadata?.Name})";
        /// <inheritdoc/>
        public void Load()
        {
            if (IsLoaded) return;

            OnLoad();

            IsLoaded = true;
        }
        /// <inheritdoc/>
        public void Unload()
        {
            if (!IsLoaded) return;

            OnUnload();

            IsLoaded = false;
        }

        protected virtual void OnLoad()
        {

        }
        protected virtual void OnUnload()
        {

        }
    }

    [DataContract]
    public abstract class PropertyElement<T> : PropertyElement where T : PropertyElementMetadata
    {
        public new T? PropertyMetadata
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
