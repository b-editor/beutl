// ComparisonKind.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics
{
    /// <summary>
    /// Describes how new values are compared with existing values in a depth or stencil comparison.
    /// </summary>
    public enum ComparisonKind : byte
    {
        /// <summary>
        /// The comparison never succeeds.
        /// </summary>
        Never = 0,

        /// <summary>
        /// The comparison succeeds when the new value is less than the existing value.
        /// </summary>
        Less = 1,

        /// <summary>
        /// The comparison succeeds when the new value is equal to the existing value.
        /// </summary>
        Equal = 2,

        /// <summary>
        /// The comparison succeeds when the new value is less than or equal to the existing value.
        /// </summary>
        LessEqual = 3,

        /// <summary>
        /// The comparison succeeds when the new value is greater than the existing value.
        /// </summary>
        Greater = 4,

        /// <summary>
        /// The comparison succeeds when the new value is not equal to the existing value.
        /// </summary>
        NotEqual = 5,

        /// <summary>
        /// The comparison succeeds when the new value is greater than or equal to the existing value.
        /// </summary>
        GreaterEqual = 6,

        /// <summary>
        /// The comparison always succeeds.
        /// </summary>
        Always = 7,
    }
}