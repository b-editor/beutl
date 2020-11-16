using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.ObjectModel
{
    public interface IParent<out T>
    {
        /// <summary>
        /// 子要素を取得します
        /// </summary>
        public IEnumerable<T> Children { get; }
    }

    public interface IChild<out T>
    {
        /// <summary>
        /// 親要素を取得します
        /// </summary>
        public T Parent { get; }
    }
}
