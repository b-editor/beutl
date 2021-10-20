using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data.Property;

namespace BEditor.ViewModels.Properties
{
    public sealed class GradientPropertyViewModel
    {
        public GradientPropertyViewModel(GradientProperty property)
        {
            Property = property;
        }

        public GradientProperty Property { get; }
    }
}
