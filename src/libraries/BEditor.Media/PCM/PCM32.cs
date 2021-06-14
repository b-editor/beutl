// PCM32.cs
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
    /// Represents the audio data in 32-bit float format.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct PCM32 : IPCM<PCM32>, IPCMConvertable<PCM16>, IPCMConvertable<PCMFloat>
    {
        /// <summary>
        /// Represents the audio data.
        /// </summary>
        public int Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="PCM32"/> struct.
        /// </summary>
        /// <param name="value">The audio data.</param>
        public PCM32(int value)
        {
            Value = value;
        }

        /// <summary>
        /// Converts the <see cref="PCM32"/> to a 32-bit signed integer.
        /// </summary>
        /// <param name="value">The Pcm data.</param>
        public static implicit operator int(PCM32 value)
        {
            return value.Value;
        }

        /// <summary>
        /// Converts the 32-bit signed integer to a <see cref="PCM32"/>.
        /// </summary>
        /// <param name="value">The 32-bit signed integer.</param>
        public static implicit operator PCM32(int value)
        {
            return new(value);
        }

        /// <inheritdoc/>
        public PCM32 Combine(PCM32 s)
        {
            return new(Value + s.Value);
        }

        /// <inheritdoc/>
        public void ConvertFrom(PCM16 src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertFrom(PCMFloat src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertTo(out PCM16 dst)
        {
            dst = new((short)(Value >> 16));
        }

        /// <inheritdoc/>
        public void ConvertTo(out PCMFloat dst)
        {
            dst = new((float)Value / int.MaxValue);
        }
    }
}