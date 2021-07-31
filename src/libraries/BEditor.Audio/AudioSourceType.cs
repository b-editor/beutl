namespace BEditor.Audio
{
    public enum AudioSourceType
    {
        /// <summary>
        /// Source is Static if a Buffer has been attached using AL.Source with the parameter Sourcei.Buffer.
        /// </summary>
        Static = 4136,
        /// <summary>
        /// Source is Streaming if one or more Buffers have been attached using AL.SourceQueueBuffers
        /// </summary>
        Streaming = 4137,
        /// <summary>
        /// Source is undetermined when it has a null Buffer attached
        /// </summary>
        Undetermined = 4144
    }
}