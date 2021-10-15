// BaseMenu{T}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Data;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents the custom file menu.
    /// </summary>
    /// <typeparam name="T">The type of argument.</typeparam>
    public abstract class BaseMenu<T> : EditingObject
    {
        /// <summary>
        /// Defines the <see cref="MainWindow"/> property.
        /// </summary>
        public static readonly EditingProperty<object?> MainWindowProperty
            = EditingProperty.Register<object?, BaseMenu<T>>("MainWindow", EditingPropertyOptions<object?>.Create().Notify(true));

        /// <summary>
        /// Occurs when the menu is clicked.
        /// </summary>
        public event EventHandler<T>? Clicked;

        /// <summary>
        /// Gets or sets the main window.
        /// </summary>
        public object? MainWindow
        {
            get => GetValue(MainWindowProperty);
            set => SetValue(MainWindowProperty, value);
        }

        /// <summary>
        /// Execute this menu.
        /// </summary>
        /// <param name="arg">The argument.</param>
        public void Execute(T arg)
        {
            OnExecute(arg);
            Clicked?.Invoke(this, arg);
        }

        /// <summary>
        /// Execute this menu.
        /// </summary>
        /// <param name="arg">The argument.</param>
        protected virtual void OnExecute(T arg)
        {
        }
    }
}
