using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data {
    public interface IParent<T> {
        /// <summary>
        /// 子要素を取得します
        /// </summary>
        public IEnumerable<T> Children { get; }
    }

    public interface IChild<T> {
        /// <summary>
        /// 親要素を取得します
        /// </summary>
        public T Parent { get; }
    }
}
