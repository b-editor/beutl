// IDirectProperty{TValue}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    internal interface IDirectProperty<TValue> : IDirectProperty, IEditingProperty<TValue>
    {
        object IDirectProperty.Get(IEditingObject owner)
        {
            return Get(owner)!;
        }

        void IDirectProperty.Set(IEditingObject owner, object value)
        {
            Set(owner, (TValue)value);
        }

        public new TValue Get(IEditingObject owner);

        public void Set(IEditingObject owner, TValue value);
    }
}