using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Extensions.ViewCommand;
using BEditor.Core.Plugin;

namespace TestPlugin2
{
    public partial class TestPlugin2
    {
        public IEnumerable<ICustomMenu> Menus => new ICustomMenu[]
        {
            new CustomMenu("Hello World", () => Message.Snackbar("Hello World")),
            new CustomMenu("Hello Dialog", () => Message.Dialog("Hello Dialog"))
        };
    }
}
