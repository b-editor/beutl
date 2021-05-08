using System;

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
    public class CustomMenu : ICustomMenu
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CustomMenu" /> class.
        /// </summary>
        /// <param name="Name">The string to be displayed in the UI.</param>
        /// <param name="Execute">Execute when the menu is clicked.</param>
        public CustomMenu(string Name, Action Execute)
        {
            this.Name = Name;
            this.Execute = Execute;
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <summary>
        /// Execute when the menu is clicked.
        /// </summary>
        public Action Execute { get; }

        /// <inheritdoc/>
        void ICustomMenu.Execute()
        {
            Execute?.Invoke();
        }
    }
}