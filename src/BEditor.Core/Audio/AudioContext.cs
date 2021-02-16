using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OpenTK;
using OpenTK.Audio.OpenAL;

namespace BEditor.Core.Audio
{
    /// <summary>
    /// Represents an OpenAL context.
    /// </summary>
    public class AudioContext : IDisposable
    {
        private readonly ALDevice device;
        private readonly ALContext context;

        /// <summary>
        /// Initializes a new instance of <see cref="AudioContext"/> class.
        /// </summary>
        public unsafe AudioContext()
        {
            device = ALC.OpenDevice(null);
            context = ALC.CreateContext(device, (int[])null!);
        }

        /// <summary>
        /// Get whether an object has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Set this context to current.
        /// </summary>
        public void MakeCurrent()
        {
            ALC.MakeContextCurrent(context);
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            ALC.MakeContextCurrent(ALContext.Null);
            ALC.DestroyContext(context);
            ALC.CloseDevice(device);

            IsDisposed = true;
        }
    }
}
