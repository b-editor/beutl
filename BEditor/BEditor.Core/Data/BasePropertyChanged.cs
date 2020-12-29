using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace BEditor.Core.Data
{
    /// <summary>
    /// プロパティの変更を通知するクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class BasePropertyChanged : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void SetValue<T1, T2>(T1 src, ref T1 dst, PropertyChangedEventArgs args, T2 state = default, Action<T2> action = null)
        {
            if (src == null || !src.Equals(dst))
            {
                dst = src;
                RaisePropertyChanged(args);
                action?.Invoke(state);
            }
        }
        protected void SetValue<T1>(T1 src, ref T1 dst, PropertyChangedEventArgs args, Action action = null)
        {
            if (src == null || !src.Equals(dst))
            {
                dst = src;
                RaisePropertyChanged(args);
                action?.Invoke();
            }
        }

        protected void RaisePropertyChanged(PropertyChangedEventArgs args)
        {
            if (PropertyChanged == null) return;

            PropertyChanged?.Invoke(this, args);
        }

        public IObservable<PropertyChangedEventArgs> AsObservable()
        {
            return Observable.FromEvent<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                h => (s, e) => h(e),
                h => PropertyChanged += h,
                h => PropertyChanged -= h);
        }
    }
}
