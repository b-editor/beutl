using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data
{
    /// <summary>
    /// 
    /// </summary>
    public interface IHasName
    {
        /// <summary>
        /// 名前を取得します
        /// </summary>
        public string Name { get; }
    }
}
