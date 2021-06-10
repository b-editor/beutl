// FilePathType.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents the type of file path.
    /// </summary>
    public enum FilePathType
    {
        /// <summary>
        /// File path is full path.
        /// </summary>
        FullPath,

        /// <summary>
        /// File paths are relative to the project.
        /// </summary>
        FromProject,
    }
}