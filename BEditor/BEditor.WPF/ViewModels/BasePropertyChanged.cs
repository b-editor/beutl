using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Text;

namespace BEditor.ViewModels
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

        protected virtual void RaisePropertyChanged(params string[] names)
        {

            if (PropertyChanged == null) return;

            CheckPropertyName(names);

            foreach (var name in names)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }

        [Conditional("DEBUG")]
        private void CheckPropertyName(params string[] names)
        {
            var props = GetType().GetProperties();
            foreach (var name in names)
            {
                var prop = props.Where(p => p.Name == name).SingleOrDefault();
                if (prop == null) throw new ArgumentException(name);
            }
        }

        protected void RaisePropertyChanged<T>(params Expression<Func<T>>[] propertyExpression)
        {
            RaisePropertyChanged(
                propertyExpression.Select(ex => ((MemberExpression)ex.Body).Member.Name).ToArray());
        }
    }
}
