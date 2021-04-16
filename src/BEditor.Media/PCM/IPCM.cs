using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Media.PCM
{
    public interface IPCM<T> where T : unmanaged, IPCM<T>
    {
        public T Add(T s);
    }
}