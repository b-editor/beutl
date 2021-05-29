// IPCM.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Media.PCM
{
    /// <summary>
    /// Represents audio data.
    /// </summary>
    /// <typeparam name="T">The type of the PCM class itself.</typeparam>
    public interface IPCM<T>
        where T : unmanaged, IPCM<T>
    {
        /// <summary>
        /// Returns the value with the specified data appended to this data.
        /// </summary>
        /// <param name="s">Data to be added.</param>
        /// <returns>Returns the sum of this audio data and <paramref name="s"/>.</returns>
        public T Add(T s);
    }
}