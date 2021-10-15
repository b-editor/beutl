// CustomMenu.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Plugin
{
    /// <inheritdoc cref="ICustomMenu"/>
    [Obsolete("Use BasePluginMenu.")]
    public sealed class CustomMenu : ICustomMenu
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CustomMenu" /> class.
        /// </summary>
        /// <param name="name">The string to be displayed in the UI.</param>
        /// <param name="execute">Execute when the menu is clicked.</param>
        public CustomMenu(string name, Action execute)
        {
            Name = name;
            Execute = execute;
        }

        /// <inheritdoc/>
        public string Name { get; }

#pragma warning disable SA1623
        /// <summary>
        /// Execute when the menu is clicked.
        /// </summary>
        public Action Execute { get; }
#pragma warning restore SA1623

        /// <inheritdoc/>
        void ICustomMenu.Execute()
        {
            Execute?.Invoke();
        }
    }
}