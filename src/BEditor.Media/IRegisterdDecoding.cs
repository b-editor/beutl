// IRegisterdDecoding.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.IO;
using System.Linq;

using BEditor.Media.Decoding;

namespace BEditor.Media
{
    /// <summary>
    /// Provides the ability to create a decoder.
    /// </summary>
    public interface IRegisterdDecoding
    {
        /// <summary>
        /// Gets the decoder name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Open the media file.
        /// </summary>
        /// <param name="file">File name of the media file.</param>
        /// <param name="options">The decoder settings.</param>
        /// <returns>Returns the input container opened by this method.</returns>
        public IInputContainer? Open(string file, MediaOptions options);

        /// <summary>
        /// Gets the value if the specified media file is supported.
        /// </summary>
        /// <param name="file">File name of the media file.</param>
        /// <returns>Returns a value if the specified media file is supported.</returns>
        public bool IsSupported(string file)
        {
            return SupportExtensions().Contains(Path.GetExtension(file));
        }

        /// <summary>
        /// Gets the supported extensions.
        /// </summary>
        /// <returns>Returns the supported extensions.</returns>
        public IEnumerable<string> SupportExtensions();
    }
}