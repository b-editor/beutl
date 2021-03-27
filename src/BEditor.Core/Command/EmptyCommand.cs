using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Properties;

namespace BEditor.Command
{
    /// <summary>
    /// Represents a class that implements an empty <see cref="IRecordCommand"/>.
    /// </summary>
    internal class EmptyCommand : IRecordCommand
    {
        /// <inheritdoc/>
        public void Do()
        {
        }

        /// <inheritdoc/>
        public void Redo()
        {
        }

        /// <inheritdoc/>
        public void Undo()
        {
        }
    }
}
