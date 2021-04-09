using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.Process
{
    public interface IPixelProcess
    {
        public void Invoke(int pos);
    }
}
