using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// Represents the multimedia file container used for encoding.
    /// </summary>
    public interface IOutputContainer : IDisposable
    {
        /// <summary>
        /// Gets the video streams.
        /// </summary>
        public IEnumerable<IVideoOutputStream> Video { get; }

        /// <summary>
        /// Gets the audio streams.
        /// </summary>
        public IEnumerable<IAudioOutputStream> Audio { get; }

        /// <summary>
        /// Applies a set of metadata fields to the output file.
        /// </summary>
        /// <param name="metadata">The metadata object to set.</param>
        public void SetMetadata(ContainerMetadata metadata);

        /// <summary>
        /// Adds a new video stream to the container. Usable only in encoding, before locking file.
        /// </summary>
        /// <param name="config">The stream configuration.</param>
        public void AddVideoStream(VideoEncoderSettings config);

        /// <summary>
        /// Adds a new audio stream to the container. Usable only in encoding, before locking file.
        /// </summary>
        /// <param name="config">The stream configuration.</param>
        public void AddAudioStream(AudioEncoderSettings config);

        /// <summary>
        /// Gets the default settings for video encoder.
        /// </summary>
        public VideoEncoderSettings GetDefaultVideoSettings();

        /// <summary>
        /// Gets the default settings for audio encoder.
        /// </summary>
        public AudioEncoderSettings GetDefaultAudioSettings();

        /// <summary>
        /// Creates a multimedia file.
        /// </summary>
        /// <returns>A new <see cref="MediaOutput"/>.</returns>
        public MediaOutput Create();
    }
}