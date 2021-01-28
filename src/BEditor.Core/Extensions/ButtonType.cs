using System;

namespace BEditor.Core.Extensions
{
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
