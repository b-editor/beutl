using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BEditor.Media.Encoding;

namespace BEditor.Media
{
    /// <summary>
    /// Provides the ability to create a encoder.
    /// </summary>
    public interface IRegisterdEncoding
    {
        /// <summary>
        /// Gets the decoder name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Create the media file.
        /// </summary>
        public IOutputContainer? Create(string file);

        /// <summary>
        /// Gets the value if the specified media file is supported.
        /// </summary>
        /// <param name="file">File name of the media file.</param>
        public bool IsSupported(string file)
        {
            return SupportExtensions().Contains(Path.GetExtension(file));
        }

        /// <summary>
        /// Gets the supported extensions.
        /// </summary>
        public IEnumerable<string> SupportExtensions();
    }
}