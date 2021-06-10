// PCM16.cs
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
    /// Represents the audio data in 16-bit float format.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct PCM16 : IPCM<PCM16>, IPCMConvertable<PCM32>, IPCMConvertable<PCMFloat>
    {
        /// <summary>
        /// Represents the audio data.
        /// </summary>
        public short Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="PCM16"/> struct.
        /// </summary>
        /// <param name="value">The audio data.</param>
        public PCM16(short value)
        {
            Value = value;
        }

        /// <summary>
        /// Converts the <see cref="PCMFloat"/> to a 16-bit signed integer.
        /// </summary>
        /// <param name="value">The Pcm data.</param>
        public static implicit operator short(PCM16 value)
        {
            return value.Value;
        }

        /// <summary>
        /// Converts the 16-bit signed integer to a <see cref="PCM16"/>.
        /// </summary>
        /// <param name="value">The 16-bit signed integer.</param>
        public static implicit operator PCM16(short value)
        {
            return new(value);
        }

        /// <inheritdoc/>
        public PCM16 Add(PCM16 s)
        {
            return new((short)(Value + s.Value));
        }

        /// <inheritdoc/>
        public void ConvertFrom(PCM32 src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertFrom(PCMFloat src)
        {
            src.ConvertTo(out this);
        }

        /// <inheritdoc/>
        public void ConvertTo(out PCM32 dst)
        {
            dst = new(Value << 16);
        }

        /// <inheritdoc/>
        public void ConvertTo(out PCMFloat dst)
        {
            dst = new((float)Value / short.MaxValue);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Value.ToString();
        }
    }
}