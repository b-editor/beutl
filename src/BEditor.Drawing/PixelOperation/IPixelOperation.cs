using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.PixelOperation
{
    public interface IPixelOperation
    {
        public void Invoke(int pos);
    }
}