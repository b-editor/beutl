using BEditor.Media.Decoding;

namespace BEditor.Media
{
    /// <summary>
    /// Provides the ability to create a decoder.
    /// </summary>
    public interface IDecoderBuilder
    {
        /// <summary>
        /// Gets the decoder name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the supported formats.
        /// </summary>
        public string[] SupportFormats { get; }

        /// <summary>
        /// Open the media file.
        /// </summary>
        /// <param name="file">File name of the media file.</param>
        /// <param name="options">The decoder settings.</param>
        public IInputContainer? Open(string file, MediaOptions options);

        /// <summary>
        /// Gets the value if the specified media file is supported.
        /// </summary>
        /// <param name="file">File name of the media file.</param>
        public bool IsSupported(string file);
    }
}