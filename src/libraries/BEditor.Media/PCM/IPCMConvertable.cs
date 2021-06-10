// IPCMConvertable.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Media.PCM
{
    /// <summary>
    /// Provides the ability to convert this audio data to specified data.
    /// </summary>
    /// <typeparam name="T">The type of data that can be converted.</typeparam>
    public interface IPCMConvertable<T>
        where T : unmanaged, IPCM<T>
    {
        /// <summary>
        /// Converts to the specified type.
        /// </summary>
        /// <param name="dst">The converted data.</param>
        public void ConvertTo(out T dst);

        /// <summary>
        /// Converts from the specified type.
        /// </summary>
        /// <param name="src">The data to be converted.</param>
        public void ConvertFrom(T src);
    }
}