// DecodingRegistory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Linq;

using BEditor.Media.Decoding;

namespace BEditor.Media
{
    /// <summary>
    /// Register and record supported decoding.
    /// </summary>
    public static class DecodingRegistory
    {
        private static readonly List<IRegisterdDecoding> _registerd = new();

        /// <summary>
        /// Enumerate the registered decodings.
        /// </summary>
        /// <returns>Returns the registered decodings.</returns>
        public static IEnumerable<IRegisterdDecoding> EnumerateDecodings()
        {
            return _registerd;
        }

        /// <summary>
        /// Open the media file by specifying the file name and options.
        /// </summary>
        /// <param name="file">The media file to be opened.</param>
        /// <param name="options">The option used to open the media file.</param>
        /// <returns>Returns the input container opened by this method.</returns>
        public static IInputContainer? Open(string file, MediaOptions options)
        {
            return GuessDecodings(file).FirstOrDefault()?.Open(file, options);
        }

        /// <summary>
        /// Guess decoding from file name.
        /// </summary>
        /// <param name="file">The file name to guess.</param>
        /// <returns>Returns the guessed decodings.</returns>
        public static IRegisterdDecoding[] GuessDecodings(string file)
        {
            return _registerd.Where(i => i.IsSupported(file)).ToArray();
        }

        /// <summary>
        /// Register the decoding.
        /// </summary>
        /// <param name="decoding">The decoding to register.</param>
        public static void Register(IRegisterdDecoding decoding)
        {
            _registerd.Add(decoding);
        }
    }
}