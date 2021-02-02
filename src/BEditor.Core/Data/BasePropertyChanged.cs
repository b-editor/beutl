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
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Set the value.
        /// </summary>
        /// <typeparam name="T1">Type of the value to set</typeparam>
        /// <typeparam name="T2">Type of state</typeparam>
        /// <param name="src">source value</param>
        /// <param name="dst">destination value</param>
        /// <param name="args">Arguments used for the <see cref="PropertyChanged"/> event.</param>
        /// <param name="state">state</param>
        /// <param name="action"><paramref name="action"/> to be executed after the <see cref="PropertyChanged"/> event occurs.</param>
        protected void SetValue<T1, T2>(T1 src, ref T1 dst, PropertyChangedEventArgs args, T2 state = default, Action<T2>? action = null)
        {
            if (src == null || !src.Equals(dst))
            {
                dst = src;
                RaisePropertyChanged(args);
                action?.Invoke(state!);
            }
        }
        /// <summary>
        /// Set the value.
        /// </summary>
        /// <typeparam name="T1">Type of the value to set</typeparam>
        /// <param name="src">source value</param>
        /// <param name="dst">destination value</param>
        /// <param name="args">Arguments used for the <see cref="PropertyChanged"/> event.</param>
        /// <param name="action"><paramref name="action"/> to be executed after the <see cref="PropertyChanged"/> event occurs.</param>
        protected void SetValue<T1>(T1 src, ref T1 dst, PropertyChangedEventArgs args, Action? action = null)
        {
            if (src == null || !src.Equals(dst))
            {
                dst = src;
                RaisePropertyChanged(args);
                action?.Invoke();
            }
        }
        /// <summary>
        /// Raise the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="args">Arguments used for the <see cref="PropertyChanged"/> event.</param>
        protected void RaisePropertyChanged(PropertyChangedEventArgs args)
        {
            if (PropertyChanged == null) return;

            PropertyChanged?.Invoke(this, args);
        }
    }
}
