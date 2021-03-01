using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Data.Bindings
{
    internal static class BindingHelper
    {
        public static void AutoLoad<T>(this IBindable<T> bindable, ref string? hint)
        {
            if (hint is not null && bindable.GetBindable(hint, out var b))
            {
                bindable.Bind(b);
            }
            hint = null;
        }
    }
}
