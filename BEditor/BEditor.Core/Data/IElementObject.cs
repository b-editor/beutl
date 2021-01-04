using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data
{
    public interface IElementObject
    {
        public void Loaded();
        public void Unloaded();
    }
}
