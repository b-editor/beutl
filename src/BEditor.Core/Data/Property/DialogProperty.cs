using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data.Property
{
    [DataContract]
    public abstract class DialogProperty : Group
    {
        public event EventHandler Showed = delegate { };

        public void Show()
        {
            Showed(this, EventArgs.Empty);
        }
    }
}
