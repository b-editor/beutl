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
        /// Gets a value that indicates whether this element has been loaded for presentation.
        /// </summary>
        public bool IsLoaded { get; }

        public void Load();
        public void Unload();
    }
}
