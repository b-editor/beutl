using System.Collections.Generic;
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
        public static IEnumerable<IRegisterdEncoding> EnumerateEncodings()
        {
            return _registerd;
        }

        /// <summary>
        /// Create a container from the name of the file to be output.
        /// </summary>
        /// <param name="file">The file name to output.</param>
        public static IOutputContainer? Create(string file)
        {
            return GuessEncodings(file).FirstOrDefault()?.Create(file);
        }

        /// <summary>
        /// Guess encoding from file name.
        /// </summary>
        /// <param name="file">The file name to guess.</param>
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