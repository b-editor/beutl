// MediaOptions.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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

        /// <summary>
        /// Gets or sets the sample rate.
        /// </summary>
        public int SampleRate { get; set; } = 44100;
    }
}