// StereoPCMFloat.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.InteropServices;

namespace BEditor.Media.PCM
{
    /// <summary>
    /// Represents the stereo audio data in 32-bit float format.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct StereoPCMFloat : IPCM<StereoPCMFloat>, IPCMConvertable<StereoPCM32>, IPCMConvertable<StereoPCM16>
    {
        /// <summary>
        /// Represents the audio data of the left channel.
        /// </summary>
        public float Left;

        /// <summary>
        /// Represents the audio data of the right channel.
        /// </summary>
        public float Right;

        /// <summary>
        /// Initializes a new instance of the <see cref="StereoPCMFloat"/> struct.
        /// </summary>
        /// <param name="left">The audio data of the left channel.</param>
        /// <param name="right">The audio data of the right channel.</param>
        public StereoPCMFloat(float left, float right)
        {
            Left = left;
            Right = right;
        }

        /// <inheritdoc/>
        public StereoPCMFloat Combine(StereoPCMFloat s)
        {
            return new(Left + s.Left, Right + s.Right);
        }

        /// <inheritdoc/>
        public void ConvertFrom(StereoPCM32 src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertFrom(StereoPCM16 src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertTo(out StereoPCM32 dst)
        {
            dst = new((int)(Left * int.MaxValue), (int)(Right * int.MaxValue));
        }

        /// <inheritdoc/>
        public void ConvertTo(out StereoPCM16 dst)
        {
            dst = new((short)(Left * short.MaxValue), (short)(Right * short.MaxValue));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Left = {Left}, Right = {Right}";
        }
    }
}