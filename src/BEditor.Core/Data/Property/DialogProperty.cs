// DialogProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a base class for showing multiple <see cref="PropertyElement"/> in a dialog.
    /// </summary>
    public abstract class DialogProperty : Group
    {
        /// <summary>
        /// Occurs after the dialog is shown.
        /// </summary>
        public event EventHandler? Showed;

        /// <summary>
        /// Show the dialog.
        /// </summary>
        public void Show()
        {
            Showed?.Invoke(this, EventArgs.Empty);
        }
    }
}