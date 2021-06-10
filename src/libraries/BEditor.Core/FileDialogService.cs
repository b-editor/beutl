// FileDialogService.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace BEditor
{
    /// <summary>
    /// Represents a service in a file dialog.
    /// </summary>
    public interface IFileDialogService
    {
        /// <summary>
        /// Show the Save File dialog.
        /// </summary>
        /// <param name="record">A record that sets the name of the selected file.</param>
        /// <returns>Returns true if the save button is clicked, false otherwise.</returns>
        public ValueTask<bool> ShowSaveFileDialogAsync(SaveFileRecord record);

        /// <summary>
        /// Show the Open File dialog.
        /// </summary>
        /// <param name="record">A record that sets the name of the selected file.</param>
        /// <returns>Returns true if the open button is clicked, false otherwise.</returns>
        public ValueTask<bool> ShowOpenFileDialogAsync(OpenFileRecord record);
    }

    /// <summary>
    /// Represents the record to be used when showing the Save File dialog.
    /// </summary>
    public record SaveFileRecord : FileDialogRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SaveFileRecord"/> class.
        /// </summary>
        /// <param name="filename">The file name.</param>
        public SaveFileRecord(string filename = "")
            : base(new List<FileFilter>())
        {
            FileName = filename;
        }

        /// <summary>
        /// Gets or sets the default file name.
        /// </summary>
        public string DefaultFileName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the file name.
        /// </summary>
        public string FileName { get; set; }
    }

    /// <summary>
    /// Represents the record to be used when showing the Open File dialog.
    /// </summary>
    public record OpenFileRecord : FileDialogRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenFileRecord"/> class.
        /// </summary>
        public OpenFileRecord()
            : base(new List<FileFilter>())
        {
        }

        /// <summary>
        /// Gets or sets the default file name.
        /// </summary>
        public string DefaultFileName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the file name.
        /// </summary>
        public string FileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents the extension of the file.
    /// </summary>
    public record FileExtension
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileExtension"/> class.
        /// </summary>
        /// <param name="value">The file extension.</param>
        public FileExtension(string value)
        {
            Value = value;
        }

        /// <summary>
        /// Gets the file extension.
        /// </summary>
        public string Value { get; init; }
    }

    /// <summary>
    /// Represents the filter of the file dialog.
    /// </summary>
    public record FileFilter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileFilter"/> class.
        /// </summary>
        /// <param name="name">The filter name.</param>
        /// <param name="extensions">The filter extensions.</param>
        public FileFilter(string name, IEnumerable<FileExtension> extensions)
        {
            Name = name;
            Extensions = extensions;
        }

        /// <summary>
        /// Gets the filter name.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the filter extensions.
        /// </summary>
        public IEnumerable<FileExtension> Extensions { get; init; }
    }

    /// <summary>
    /// Represents the base class to be used when showing the File dialog.
    /// </summary>
    public record FileDialogRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileDialogRecord"/> class.
        /// </summary>
        /// <param name="filters">The filter for the file dialog.</param>
        public FileDialogRecord(List<FileFilter> filters)
        {
            Filters = filters;
        }

        /// <summary>
        /// Gets the filter for the file dialog.
        /// </summary>
        public List<FileFilter> Filters { get; init; }
    }
}