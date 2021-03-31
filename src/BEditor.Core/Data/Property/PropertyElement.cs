using System;
using System.ComponentModel;

using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property used by <see cref="EffectElement"/>.
    /// </summary>
    public class PropertyElement : EditingObject, IChild<EffectElement>, IPropertyElement, IHasName
    {
        private static readonly PropertyChangedEventArgs _metadataArgs = new(nameof(PropertyMetadata));
        private PropertyElementMetadata? _propertyMetadata;
        private int? _id;
        private WeakReference<EffectElement?>? _parent;

        /// <inheritdoc/>
        public virtual EffectElement Parent
        {
            get
            {
                _parent ??= new(null!);

                if (_parent.TryGetTarget(out var p))
                {
                    return p;
                }

                return null!;
            }
            set => (_parent ??= new(null!)).SetTarget(value);
        }

        /// <summary>
        /// Gets or sets the metadata for this <see cref="PropertyElement"/>.
        /// </summary>
        public PropertyElementMetadata? PropertyMetadata
        {
            get => _propertyMetadata;
            set => SetValue(value, ref _propertyMetadata, _metadataArgs);
        }

        /// <inheritdoc/>
        public int Id => (_id ??= Parent?.Children?.IndexOf(this)) ?? -1;

        /// <inheritdoc/>
        public string Name => _propertyMetadata?.Name ?? Id.ToString();

        /// <inheritdoc/>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            static bool IsInner(PropertyElement bindable, out int groupId)
            {
                groupId = -1;
                foreach (var item in bindable.Parent.Children)
                {
                    if (item == bindable)
                    {
                        return false;
                    }

                    if (item is IParent<PropertyElement> inner_prop)
                    {
                        foreach (var inner_item in inner_prop.Children)
                        {
                            if (inner_prop is IHasId hasId) groupId = hasId.Id;

                            if (inner_item == bindable)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            format ??= string.Empty;
            if (format.ToUpperInvariant() is "#" or "fullname")
            {
                if (IsInner(this, out var groupId))
                {
                    // 親がGroup
                    // Idは-1
                    var scene = this.GetParent3()?.Name ?? throw new DataException(Strings.ParentElementNotFound);
                    var clip = this.GetParent2()?.Name ?? throw new DataException(Strings.ParentElementNotFound);
                    var effect = this.GetParent()?.Id ?? throw new DataException(Strings.ParentElementNotFound);

                    return $"{scene}.{clip}[{effect}][{groupId}][{Id}]";
                }

                return $"{this.GetParent3()?.Name}.{this.GetParent2()?.Name}[{this.GetParent()?.Id}][{Id}]";
            }

            return GetType().FullName!;
        }
    }
}
