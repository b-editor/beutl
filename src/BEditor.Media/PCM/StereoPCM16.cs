using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.PCM
{
    /// <summary>
    /// Represents 16-bit integer audio data.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct StereoPCM16 : IPCM<StereoPCM16>, IPCMConvertable<StereoPCM32>, IPCMConvertable<StereoPCMFloat>
    {
        /// <summary>
        /// Represents the audio data of the left channel.
        /// </summary>
        public short Left;

        /// <summary>
        /// Represents the audio data of the right channel.
        /// </summary>
        public short Right;

        /// <summary>
        /// Initializes a new instance of the <see cref="StereoPCM16"/> class.
        /// </summary>
        /// <param name="left">The audio data of the left channel.</param>
        /// <param name="right">The audio data of the right channel.</param>
        public StereoPCM16(short left, short right)
        {
            Left = left;
            Right = right;
        }

        /// <inheritdoc/>
        public StereoPCM16 Add(StereoPCM16 s)
        {
            return new((short)(Left + s.Left), (short)(Right + s.Right));
        }

        /// <inheritdoc/>
        public void ConvertFrom(StereoPCM32 src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertFrom(StereoPCMFloat src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertTo(out StereoPCM32 dst)
        {
            dst = new(Left << 16, Right << 16);
        }

        /// <inheritdoc/>
        public void ConvertTo(out StereoPCMFloat dst)
        {
            dst = new((float)Left / short.MaxValue, (float)Right / short.MaxValue);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Left = {Left}, Right = {Right}";
        }
    }
}