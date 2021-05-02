using System;
using System.Collections.Generic;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents a chapter in a media file.
    /// </summary>
    public class MediaChapter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MediaChapter"/> class.
        /// </summary>
        /// <param name="start">The starting time of this chapter.</param>
        /// <param name="end">The ending time of this chapter.</param>
        /// <param name="metadata">This chapter's metadata.</param>
        internal MediaChapter(TimeSpan start, TimeSpan end, Dictionary<string, string> metadata)
        {
            StartTime = start;
            EndTime = end;
            Metadata = metadata;
        }

        /// <summary>
        /// Gets the start time of this chapter.
        /// </summary>
        public TimeSpan StartTime { get; }

        /// <summary>
        /// Gets the end time of this chapter.
        /// </summary>
        public TimeSpan EndTime { get; }

        /// <summary>
        /// Gets the duration of this chapter.
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// Gets the metadata for this chapter (such as name).
        /// </summary>
        public Dictionary<string, string> Metadata { get; }
    }
}