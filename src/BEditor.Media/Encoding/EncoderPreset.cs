using System.ComponentModel;

namespace BEditor.Media.Encoding
{
    /// <summary>
    /// The presets for H264 and H265 (HEVC) video encoders.
    /// <para>Fast presets = faster encoding, worse compression.</para>
    /// <para>Slow presets = longer encoding, better compression.</para>
    /// </summary>
    public enum EncoderPreset
    {
        /// <summary>
        /// Port of 'ultrafast'
        /// </summary>
        [Description("ultrafast")]
        UltraFast = 0,

        /// <summary>
        /// Port of 'superfast'
        /// </summary>
        [Description("superfast")]
        SuperFast = 1,

        /// <summary>
        /// Port of 'veryfast'
        /// </summary>
        [Description("veryfast")]
        VeryFast = 2,

        /// <summary>
        /// Port of 'faster'
        /// </summary>
        [Description("faster")]
        Faster = 3,

        /// <summary>
        /// Port of 'fast'
        /// </summary>
        [Description("fast")]
        Fast = 4,

        /// <summary>
        /// The default preset. Port of 'medium'
        /// </summary>
        [Description("medium")]
        Medium = 5,

        /// <summary>
        /// Port of 'slow'
        /// </summary>
        [Description("slow")]
        Slow = 6,

        /// <summary>
        /// Port of 'slower'
        /// </summary>
        [Description("slower")]
        Slower = 7,

        /// <summary>
        /// Port of 'veryslow'
        /// </summary>
        [Description("veryslow")]
        VerySlow = 8,
    }
}