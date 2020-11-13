using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;


namespace BEditor.ViewModels.Helper
{
    /// <summary>
    /// できるだけNugetを少なくしたいので作ったクラス
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DelegateProperty<T> : BasePropertyChanged
    {
        private T value;


        public T Value { get => value; set => SetValue(value, ref this.value, nameof(Value)); }

        public DelegateProperty()
        {

        }

        public DelegateProperty(T value)
        {
            this.value = value;
        }
        public DelegateProperty(T value, Action action)
        {
            this.value = value;
            Subscribe(action);
        }

        public void Subscribe(Action action)
        {
            PropertyChanged += (_, _) =>
            {
                action?.Invoke();
            };
        }
    }
}
