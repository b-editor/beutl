// IMessage.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Threading.Tasks;

namespace BEditor
{
    /// <summary>
    /// Represents a class that provides a service to notify users.
    /// </summary>
    public interface IMessage
    {
        /// <summary>
        /// Represents the type of icon.
        /// </summary>
        public enum IconType
        {
            /// <summary>
            /// The none.
            /// </summary>
            None,

            /// <summary>
            /// The info.
            /// </summary>
            Info,

            /// <summary>
            /// The success.
            /// </summary>
            Success,

            /// <summary>
            /// The warning.
            /// </summary>
            Warning,

            /// <summary>
            /// The error.
            /// </summary>
            Error,
        }

        /// <summary>
        /// Represents a type of button.
        /// </summary>
        public enum ButtonType
        {
            /// <summary>
            /// Ok.
            /// </summary>
            Ok = 1,

            /// <summary>
            /// Yes.
            /// </summary>
            Yes = 2,

            /// <summary>
            /// No.
            /// </summary>
            No = 4,

            /// <summary>
            /// Cancel.
            /// </summary>
            Cancel = 8,

            /// <summary>
            /// Retry.
            /// </summary>
            Retry = 16,

            /// <summary>
            /// Close.
            /// </summary>
            Close = 32,
        }

        /// <summary>
        /// Show the dialog.
        /// </summary>
        /// <param name="text">The string to display.</param>
        /// <param name="icon">The icon to display.</param>
        /// <param name="types">The type of button to display.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public ValueTask<ButtonType?> DialogAsync(string text, IconType icon = IconType.Info, ButtonType[]? types = null);

        /// <summary>
        /// Show the snackbar.
        /// </summary>
        /// <param name="text">The string to display.</param>
        public void Snackbar(string text = "");

        /// <summary>
        /// Show the snackbar.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="title">The title.</param>
        /// <param name="icon">The icon.</param>
        /// <param name="close">The function invoked when the snack bar is closed.</param>
        /// <param name="action">The function invoked when the snack bar is clicked.</param>
        /// <param name="actionName">The name of <paramref name="action"/>.</param>
        /// <param name="parameter">The command parameter.</param>
        public void Snackbar(
            string text,
            string title,
            IconType icon = IconType.Info,
            Action? close = null,
            Action<object?>? action = null,
            string actionName = "",
            object? parameter = null);
    }
}