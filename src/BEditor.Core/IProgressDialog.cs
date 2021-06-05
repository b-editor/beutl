// IProgressDialog.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor
{
    /// <summary>
    /// Represents a progress dialog.
    /// </summary>
    public interface IProgressDialog : IProgress<int>
    {
        /// <summary>
        /// Gets or sets the maximum value of the progress bar.
        /// </summary>
        public double Maximum { get; set; }

        /// <summary>
        /// Gets or sets the minimum value of the progress bar.
        /// </summary>
        public double Minimum { get; set; }

        /// <summary>
        /// Gets or sets the current value of the progress bar.
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// Gets or sets the string to be displayed in the dialog.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Gets or sets whether the progress bar shows actual values or generic, continuous progress feedback.
        /// </summary>
        public bool IsIndeterminate { get; set; }
    }
}