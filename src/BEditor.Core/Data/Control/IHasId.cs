using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Data
{
    /// <summary>
    /// 
    /// </summary>
    public interface IHasId
    {
        /// <summary>
        /// Idを取得します
        /// </summary>
        public int Id { get; }
    }
}
