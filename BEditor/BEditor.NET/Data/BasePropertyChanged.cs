using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Text;

namespace BEditor.NET.Data {

    /// <summary>
    /// プロパティの変更を通知するクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class BasePropertyChanged : INotifyPropertyChanged {

        public event PropertyChangedEventHandler PropertyChanged;

        protected void SetValue<T>(T src, ref T dst, string name) {
            if (src == null || !src.Equals(dst)) {
                dst = src;
                RaisePropertyChanged(name);
            }
        }

        protected void RaisePropertyChanged(string name) {
            if (PropertyChanged == null) return;

            PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
