using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Plugin
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

    /// <inheritdoc cref="ICustomMenu"/>
    public record CustomMenu : ICustomMenu
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CustomMenu" /> class.
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="Execute"></param>
        public CustomMenu(string Name, Action Execute)
        {
            this.Name = Name;
            this.Execute = Execute;
        }

        /// <inhritdoc/>
        public string Name { get; }
        /// <summary>
        /// Execute when the menu is clicked.
        /// </summary>
        public Action Execute { get; }

        void ICustomMenu.Execute()
        {
            Execute?.Invoke();
        }
    }
}
