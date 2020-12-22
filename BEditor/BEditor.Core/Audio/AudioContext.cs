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
        private readonly IntPtr device;
        private readonly ContextHandle context;

        public unsafe AudioContext()
        {
            int* NULL = null;
            device = Alc.OpenDevice(null);
            context = Alc.CreateContext(device, NULL);

            Alc.MakeContextCurrent(context);
        }

        public bool IsDisposed { get; private set; }

        public void MakeCurrent()
        {
            Alc.MakeContextCurrent(context);
        }
        public void Dispose()
        {
            if (IsDisposed) return;

            Alc.MakeContextCurrent(ContextHandle.Zero);
            Alc.DestroyContext(context);
            Alc.CloseDevice(device);

            IsDisposed = true;
        }
    }
}
