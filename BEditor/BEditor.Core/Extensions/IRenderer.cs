using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Extensions
{
    public interface IRenderer
    {
        public void OnCompleted();
        public void OnError(Exception error);
        public void OnNext();
    }
}
