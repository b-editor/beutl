using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data
{
    public interface IElementObject
    {
        /// <summary>
        /// Gets a value that indicates whether this <see cref="IElementObject"/> has been loaded for presentation.
        /// </summary>
        public bool IsLoaded { get; }

        /// <summary>
        /// Activate this <see cref="IElementObject"/>.
        /// </summary>
        public void Load();
        /// <summary>
        /// Disables this <see cref="IElementObject"/>.
        /// </summary>
        public void Unload();
    }
}
