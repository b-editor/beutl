using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data.PropertyData.Bind
{
    public interface IBindable<T> : IObservable<T>, IObserver<T>
    {
        public BindMode Mode { get; set; }
        public string BindHint { get; }
        public T Value { get; }
    }

    public enum BindMode
    {
        TwoWay,
        OneWay,
    }
}
