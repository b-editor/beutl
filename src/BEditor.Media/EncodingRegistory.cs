// EncodingRegistory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

using BEditor.Media.Encoding;

namespace BEditor.Media
{
    /// <summary>
    /// Register and record supported encoding.
    /// </summary>
    public static class EncodingRegistory
    {
        private static readonly List<IRegisterdEncoding> _registerd = new();

        /// <summary>
        /// Enumerate the registered encoding.
        /// </summary>
        /// <returns>Returns the registered encodings.</returns>
        public static IEnumerable<IRegisterdEncoding> EnumerateEncodings()
        {
            return _registerd;
        }

        /// <summary>
        /// Create a container from the name of the file to be output.
        /// </summary>
        /// <param name="file">The file name to output.</param>
        /// <returns>Returns the output container created by this method.</returns>
        public static IOutputContainer? Create(string file)
        {
            var encoding = GuessEncodings(file).FirstOrDefault();
            return encoding?.Create(file);
        }

        /// <summary>
        /// Create a container from the name of the file to be output.
        /// </summary>
        /// <param name="file">The file name to output.</param>
        /// <param name="container">Returns the output container created by this method.</param>
        /// <param name="encoding">The encoding.</param>
        public static void Create(
            string file,
            [NotNullIfNotNull("encoding")] out IOutputContainer? container,
            [NotNullIfNotNull("container")] out IRegisterdEncoding? encoding)
        {
            encoding = GuessEncodings(file).FirstOrDefault()!;
            container = encoding?.Create(file);
        }

        /// <summary>
        /// Guess encoding from file name.
        /// </summary>
        /// <param name="file">The file name to guess.</param>
        /// <returns>Returns the guessed encodings.</returns>
        public static IRegisterdEncoding[] GuessEncodings(string file)
        {
            return _registerd.Where(i => i.IsSupported(file)).ToArray();
        }

        /// <summary>
        /// Register the encoding.
        /// </summary>
        /// <param name="encoding">The encoding to register.</param>
        public static void Register(IRegisterdEncoding encoding)
        {
            _registerd.Add(encoding);
        }
    }
}