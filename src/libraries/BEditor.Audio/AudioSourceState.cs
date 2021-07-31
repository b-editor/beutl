namespace BEditor.Audio
{
    public enum AudioSourceState
    {
        /// <summary>
        /// Default State when loaded, can be manually set with AL.SourceRewind().
        /// </summary>
        Initial = 4113,
        /// <summary>
        /// The source is currently playing.
        /// </summary>
        Playing = 4114,
        /// <summary>
        /// The source has paused playback.
        /// </summary>
        Paused = 4115,
        /// <summary>
        /// The source is not playing.
        /// </summary>
        Stopped = 4116
    }
}