using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Plugin
{
    /// <summary>
    /// Represents a menu.
    /// </summary>
    public interface ICustomMenu
    {
        /// <summary>
        /// Get the string to be displayed in the UI.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Execute when the menu is clicked.
        /// </summary>
        public void Execute();
    }

#pragma warning disable CS1591 // 公開されている型またはメンバーの XML コメントがありません
    /// <inheritdoc cref="ICustomMenu"/>
    public record CustomMenu(string Name, Action Execute) : ICustomMenu
    {
        void ICustomMenu.Execute()
        {
            Execute?.Invoke();
        }
    }
#pragma warning restore CS1591 // 公開されている型またはメンバーの XML コメントがありません
}
