// IMediaStream.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// A base for streams of any kind of media.
    /// </summary>
    public interface IMediaStream : IDisposable
    {
        /// <summary>
        /// Gets informations about this stream.
        /// </summary>
        public StreamInfo Info { get; }
    }
}