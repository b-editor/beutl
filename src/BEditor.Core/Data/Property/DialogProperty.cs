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
