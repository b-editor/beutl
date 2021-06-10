// IEditingObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.ComponentModel;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the edited data.
    /// </summary>
    public interface IEditingObject : INotifyPropertyChanged, IElementObject
    {
        /// <summary>
        /// Gets the ServiceProvider.
        /// </summary>
        public IServiceProvider? ServiceProvider { get; }

        /// <summary>
        /// Gets the ID.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Gets or sets the local value of <see cref="EditingProperty"/>.
        /// </summary>
        /// <param name="property">The <see cref="EditingProperty"/> identifier of the property whose value is to be set or retrieved.</param>
        /// <returns>Returns the current effective value.</returns>
        public object? this[EditingProperty property] { get; set; }

        /// <summary>
        /// Gets the local value of <see cref="EditingProperty{TValue}"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <param name="property">The <see cref="EditingProperty{TValue}"/> identifier of the property to retrieve the value for.</param>
        /// <returns>Returns the current effective value.</returns>
        public TValue GetValue<TValue>(EditingProperty<TValue> property);

        /// <summary>
        /// Gets the local value of <see cref="EditingProperty"/>.
        /// </summary>
        /// <param name="property">The <see cref="EditingProperty"/> identifier of the property to retrieve the value for.</param>
        /// <returns>Returns the current effective value.</returns>
        public object? GetValue(EditingProperty property);

        /// <summary>
        /// Sets the local value of <see cref="EditingProperty{TValue}"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the local value.</typeparam>
        /// <param name="property">The identifier of the <see cref="EditingProperty{TValue}"/> to set.</param>
        /// <param name="value">The new local value.</param>
        public void SetValue<TValue>(EditingProperty<TValue> property, TValue value);

        /// <summary>
        /// Sets the local value of <see cref="EditingProperty"/>.
        /// </summary>
        /// <param name="property">The identifier of the <see cref="EditingProperty"/> to set.</param>
        /// <param name="value">The new local value.</param>
        public void SetValue(EditingProperty property, object? value);

        /// <summary>
        /// Removes all local values from this <see cref="EditingObject"/>.
        /// </summary>
        public void ClearDisposable();

        /// <summary>
        /// Update the Id of all child elements that contain this object.
        /// </summary>
        public void UpdateId();
    }
}