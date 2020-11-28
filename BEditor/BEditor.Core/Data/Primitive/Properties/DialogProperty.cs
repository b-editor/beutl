using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data.Primitive.Properties
{
    [DataContract(Namespace = "")]
    public abstract class DialogProperty : Group
    {
        public event EventHandler Showed;
        public event EventHandler Closed;

        public void Show()
        {
            Showed?.Invoke(this, EventArgs.Empty);
        }
        public void Close()
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
