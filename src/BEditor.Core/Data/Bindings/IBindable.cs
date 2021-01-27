using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Bindings
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T">Type of object to bind</typeparam>
    public interface IBindable<T> : IBindable, IObservable<T>, IObserver<T>
    {
        /// <summary>
        /// Get a value.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// このオブジェクトと <paramref name="bindable"/> をバインドします。
        /// </summary>
        /// <param name="bindable"></param>
        public void Bind(IBindable<T>? bindable);
    }
    /// <summary>
    /// 
    /// </summary>
    public interface IBindable : IPropertyElement
    {
        /// <summary>
        /// Bindするオブジェクトを検索する時に使用するヒントを取得します。
        /// </summary>
        public string? BindHint { get; }
    }
}
