namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents the multimedia file container options.
    /// </summary>
    public class MediaOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MediaOptions"/> class.
        /// </summary>
        public MediaOptions()
        {
        }

        /// <summary>
        /// Gets or sets which streams (audio/video) will be loaded.
        /// </summary>
        public MediaMode StreamsToLoad { get; set; } = MediaMode.AudioVideo;
    }
}