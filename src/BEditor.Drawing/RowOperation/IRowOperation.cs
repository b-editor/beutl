// IRowOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing.RowOperation
{
    /// <summary>
    /// Represents a rows operation.
    /// </summary>
    public interface IRowOperation
    {
        /// <summary>
        /// Operate on a single row.
        /// </summary>
        /// <param name="y">The index of the rows to operate on.</param>
        public void Invoke(int y);
    }
}