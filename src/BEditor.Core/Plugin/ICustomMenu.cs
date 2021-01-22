using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Plugin
{
    public interface ICustomMenuPlugin : IPlugin
    {
        public IEnumerable<ICustomMenu> Menus { get; }
    }

    public interface ICustomMenu
    {
        public string Name { get; }

        public void Execute();
    }

    public record CustomMenu(string Name, Action Execute) : ICustomMenu
    {
        void ICustomMenu.Execute()
        {
            Execute?.Invoke();
        }
    }
}
