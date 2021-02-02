namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the type of rendering request.
    /// </summary>
    public enum RenderType
    {
        /// <summary>
        /// Represents the preview rendering during editing.
        /// </summary>
        Preview,
        /// <summary>
        /// Represents the rendering during playing.
        /// </summary>
        VideoPreview,
        /// <summary>
        /// Represents the rendering in the image output.
        /// </summary>
        ImageOutput,
        /// <summary>
        /// Represents the rendering in the video output.
        /// </summary>
        VideoOutput
    }
}
