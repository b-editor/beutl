using System;

namespace BEditor.Data
{
    internal interface IDirectProperty : IEditingProperty
    {
        public object Get(IEditingObject owner);
        public void Set(IEditingObject owner, object value);
    }

    /// <summary>
    /// A direct editing property.
    /// </summary>
    /// <typeparam name="TOwner">The type of the owner.</typeparam>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    public class DirectEditingProperty<TOwner, TValue> : EditingProperty<TValue>, IDirectProperty
        where TOwner : IEditingObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DirectEditingProperty{TOwner, TValue}"/> class.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="getter"></param>
        /// <param name="setter"></param>
        public DirectEditingProperty(string name, Func<TOwner, TValue> getter, Action<TOwner, TValue> setter)
            : base(name, typeof(TOwner))
        {
            (Getter, Setter) = (getter, setter);
        }

        public Func<TOwner, TValue> Getter { get; }

        public Action<TOwner, TValue> Setter { get; }

        object IDirectProperty.Get(IEditingObject owner)
        {
            return Getter((TOwner)owner)!;
        }

        void IDirectProperty.Set(IEditingObject owner, object value)
        {
            Setter((TOwner)owner, (TValue)value);
        }
    }
}
