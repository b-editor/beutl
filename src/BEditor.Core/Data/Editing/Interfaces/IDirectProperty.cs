// IDirectProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    internal interface IDirectProperty : IEditingProperty
    {
        public object Get(IEditingObject owner);

        public void Set(IEditingObject owner, object value);
    }
}