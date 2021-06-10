// ICustomMenu.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents a menu.
    /// </summary>
    public interface ICustomMenu
    {
        /// <summary>
        /// Get the string to be displayed in the UI.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Execute when the menu is clicked.
        /// </summary>
        public void Execute();
    }

    /// <inheritdoc cref="ICustomMenu"/>
    public class CustomMenu : ICustomMenu
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

        /// <summary>
        /// Execute when the menu is clicked.
        /// </summary>
        public Action Execute { get; }

        /// <inheritdoc/>
        void ICustomMenu.Execute()
        {
            Execute?.Invoke();
        }
    }
}