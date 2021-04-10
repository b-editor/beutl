using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.RowOperation
{
    public interface IRowOperation
    {
        public void Invoke(int y);
    }
}
