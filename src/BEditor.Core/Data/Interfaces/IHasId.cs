using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the object with id.
    /// </summary>
    public interface IHasId
    {
        /// <summary>
        /// Gets the Id of this object.
        /// </summary>
        public int Id { get; }
    }
}