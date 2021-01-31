using System;
using System.Collections.Generic;
using System.Text;

namespace BEditor.Core.Extensions
{
    /// <summary>
    /// Represents a class that provides a service to notify users.
    /// </summary>
    public static class Message
    {
        /// <summary>
        /// Occurs when showing a dialog.
        /// </summary>
        public static event Func<string, IconType, ButtonType[], ButtonType>? DialogFunc;
        /// <summary>
        /// Occurs when showing a snackbar.
        /// </summary>
        public static event Action<string>? SnackberFunc;

        /// <summary>
        /// Show the dialog.
        /// </summary>
        public static ButtonType? Dialog(string text, IconType icon = IconType.Info, ButtonType[]? types = null)
        {
            if (types is null) types = new ButtonType[] { ButtonType.Ok, ButtonType.Close };

            return DialogFunc?.Invoke(text, icon, types);
        }
        /// <summary>
        /// Show the snackbar.
        /// </summary>
        public static void Snackbar(string text = "") => SnackberFunc?.Invoke(text);
    }
}
