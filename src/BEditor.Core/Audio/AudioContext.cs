using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OpenTK;
using OpenTK.Audio.OpenAL;

namespace BEditor.Core.Audio
{
    public class AudioContext : IDisposable
    {
        private readonly ALDevice device;
        private readonly ALContext context;

        public unsafe AudioContext()
        {
            int* NULL = null;
            device = ALC.OpenDevice(null);
            context = ALC.CreateContext(device, NULL);

            ALC.MakeContextCurrent(context);
        }

        public bool IsDisposed { get; private set; }

        public void MakeCurrent()
        {
            ALC.MakeContextCurrent(context);
        }
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
