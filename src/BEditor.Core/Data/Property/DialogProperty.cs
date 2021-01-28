using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// Represents a base class for showing multiple <see cref="PropertyElement"/> in a dialog.
    /// </summary>
    [DataContract]
    public abstract class DialogProperty : Group
    {
        /// <summary>
        /// Occurs after the dialog is shown.
        /// </summary>
        public event EventHandler? Showed;

        /// <summary>
        /// Show the dialog
        /// </summary>
        public void Show()
        {
            Showed?.Invoke(this, EventArgs.Empty);
        }
    }
}
