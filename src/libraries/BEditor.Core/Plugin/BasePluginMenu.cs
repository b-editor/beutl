// BasePluginMenu.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Data;

namespace BEditor.Plugin
{
    /// <summary>
    /// Menu Location.
    /// </summary>
    public enum MenuLocation
    {
        /// <summary>
        /// The default.
        /// </summary>
        Default,

        /// <summary>
        /// The menu will be placed on the left side.
        /// </summary>
        Left,

        /// <summary>
        /// The menu will be placed on the right side.
        /// </summary>
        Right,

        /// <summary>
        /// The menu will be placed below.
        /// </summary>
        Bottom,
    }

    /// <summary>
    /// Base class for the plugin menu.
    /// </summary>
    public abstract class BasePluginMenu : BaseMenu<object?>
    {
        /// <summary>
        /// Defines the <see cref="MenuLocation"/> property.
        /// </summary>
        public static readonly EditingProperty<MenuLocation> MenuLocationProperty
            = EditingProperty.Register<MenuLocation, BasePluginMenu>("MenuLocation", EditingPropertyOptions<MenuLocation>.Create().Notify(true));

        /// <summary>
        /// Gets or sets the menu location.
        /// </summary>
        public MenuLocation MenuLocation
        {
            get => GetValue(MenuLocationProperty);
            set => SetValue(MenuLocationProperty, value);
        }

        /// <summary>
        /// Execute when the menu is clicked.
        /// </summary>
        public void Execute()
        {
            Execute(null);
        }

        /// <summary>
        /// Execute when the menu is clicked.
        /// </summary>
        protected abstract void OnExecute();

        /// <inheritdoc/>
        protected sealed override void OnExecute(object? arg)
        {
            base.OnExecute(arg);
            OnExecute();
        }
    }
}