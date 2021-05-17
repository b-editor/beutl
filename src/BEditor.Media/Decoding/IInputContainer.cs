using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Media.Decoding
{
    /// <summary>
    /// Represents the multimedia file container.
    /// </summary>
    public interface IInputContainer : IDisposable
    {
        /// <summary>
        /// Gets all streams.
        /// </summary>
        public IMediaStream[] Streams { get; }

        /// <summary>
        /// Gets informations about the media container.
        /// </summary>
        public MediaInfo Info { get; }
    }
}
