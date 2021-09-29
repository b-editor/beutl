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
    [Obsolete("Use BasePluginMenu.")]
    public interface ICustomMenu
    {
        /// <summary>
        /// Gets the string to be displayed in the UI.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Execute when the menu is clicked.
        /// </summary>
        public void Execute();
    }
}