using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Concurrency;
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

        protected void SetValue<T>(T src, ref T dst, string name)
        {
            if (src == null || !src.Equals(dst))
            {
                dst = src;
                RaisePropertyChanged(name);
            }
        }
        protected void SetValue<T>(T src, ref T dst, PropertyChangedEventArgs args, Action action = null)
        {
            if (src == null || !src.Equals(dst))
            {
                dst = src;
                RaisePropertyChanged(args);
                action?.Invoke();
            }
        }

        protected void RaisePropertyChanged(string name)
        {
            if (PropertyChanged == null) return;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        protected void RaisePropertyChanged(PropertyChangedEventArgs args)
        {
            if (PropertyChanged == null) return;

            PropertyChanged?.Invoke(this, args);
        }
    }
}
