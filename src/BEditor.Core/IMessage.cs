using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor
{
    /// <summary>
    /// Represents a class that provides a service to notify users.
    /// </summary>
    public interface IMessage
    {
        /// <summary>
        /// Show the dialog.
        /// </summary>
        public ButtonType? Dialog(string text, IconType icon = IconType.Info, ButtonType[]? types = null);
        /// <summary>
        /// Show the snackbar.
        /// </summary>
        public void Snackbar(string text = "");

        /// <summary>
        /// Represents the type of icon.
        /// </summary>
        public enum IconType
        {
            /// <summary>
            /// The info.
            /// </summary>
            Info = 3075,
            /// <summary>
            /// The none.
            /// </summary>
            None = 3695,
            /// <summary>
            /// The error.
            /// </summary>
            Error = 135
        }
        /// <summary>
        /// Represents a type of button.
        /// </summary>
        public enum ButtonType
        {
            /// <summary>
            /// Ok
            /// </summary>
            Ok = 1,
            /// <summary>
            /// Yes
            /// </summary>
            Yes = 2,
            /// <summary>
            /// No
            /// </summary>
            No = 4,
            /// <summary>
            /// Cancel
            /// </summary>
            Cancel = 8,
            /// <summary>
            /// Retry
            /// </summary>
            Retry = 16,
            /// <summary>
            /// Close
            /// </summary>
            Close = 32,
        }
    }
}
