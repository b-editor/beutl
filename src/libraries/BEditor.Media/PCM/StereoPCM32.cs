// StereoPCM32.cs
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
    /// Represents the stereo audio data in 32-bit integer format.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct StereoPCM32 : IPCM<StereoPCM32>, IPCMConvertable<StereoPCM16>, IPCMConvertable<StereoPCMFloat>
    {
        /// <summary>
        /// Represents the audio data of the left channel.
        /// </summary>
        public int Left;

        /// <summary>
        /// Represents the audio data of the right channel.
        /// </summary>
        public int Right;

        /// <summary>
        /// Initializes a new instance of the <see cref="StereoPCM32"/> struct.
        /// </summary>
        /// <param name="left">The audio data of the left channel.</param>
        /// <param name="right">The audio data of the right channel.</param>
        public StereoPCM32(int left, int right)
        {
            Left = left;
            Right = right;
        }

        /// <inheritdoc/>
        public StereoPCM32 Add(StereoPCM32 s)
        {
            return new(Left + s.Left, Right + s.Right);
        }

        /// <inheritdoc/>
        public void ConvertFrom(StereoPCM16 src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertFrom(StereoPCMFloat src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertTo(out StereoPCM16 dst)
        {
            dst = new((short)(Left >> 16), (short)(Right >> 16));
        }

        /// <inheritdoc/>
        public void ConvertTo(out StereoPCMFloat dst)
        {
            dst = new((float)Left / int.MaxValue, (float)Right / int.MaxValue);
        }
    }
}