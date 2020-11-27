using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Primitive.Components
{
    public class ButtonComponent : ComponentElement<PropertyElementMetadata>
    {
        public ButtonComponent(PropertyElementMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        public event EventHandler Click;

        public void Execute()
        {
            Click?.Invoke(this, EventArgs.Empty);
        }
    }
}
