using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Components;
using BEditor.Core.Data.Property;

using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class ButtonComponentViewModel
    {
        public ButtonComponentViewModel(ButtonComponent button)
        {
            Component = button;
            ClickCommand.Subscribe(() => Component.Execute());
        }

        public ButtonComponent Component { get; }
        public ReactiveCommand ClickCommand { get; } = new();
    }
}
