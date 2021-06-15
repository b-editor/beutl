// BasePropertyChanged.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a class that notifies property changes.
    /// </summary>
    public abstract class BasePropertyChanged : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Set the value.
        /// </summary>
        /// <typeparam name="T1">Type of the value to set.</typeparam>
        /// <typeparam name="T2">Type of state.</typeparam>
        /// <param name="src">source value.</param>
        /// <param name="dst">destination value.</param>
        /// <param name="args">Arguments used for the <see cref="PropertyChanged"/> event.</param>
        /// <param name="state">state.</param>
        /// <param name="action"><paramref name="action"/> to be executed after the <see cref="PropertyChanged"/> event occurs.</param>
        [Obsolete("Use SetAndRaise")]
        protected void SetValue<T1, T2>(T1 src, ref T1 dst, PropertyChangedEventArgs args, T2? state = default, Action<T2>? action = null)
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
        /// <typeparam name="T1">Type of the value to set.</typeparam>
        /// <param name="src">source value.</param>
        /// <param name="dst">destination value.</param>
        /// <param name="args">Arguments used for the <see cref="PropertyChanged"/> event.</param>
        /// <param name="action"><paramref name="action"/> to be executed after the <see cref="PropertyChanged"/> event occurs.</param>
        [Obsolete("Use SetAndRaise")]
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
        /// Set the value.
        /// </summary>
        /// <typeparam name="T">Type of the value to set.</typeparam>
        /// <param name="src">source value.</param>
        /// <param name="dst">destination value.</param>
        /// <param name="args">Arguments used for the <see cref="PropertyChanged"/> event.</param>
        /// <returns>Returns true if the value has been changed, false otherwise.</returns>
        protected bool SetAndRaise<T>(T src, ref T dst, PropertyChangedEventArgs args)
        {
            if (src == null || !src.Equals(dst))
            {
                dst = src;
                RaisePropertyChanged(args);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Set the value.
        /// </summary>
        /// <typeparam name="T">Type of the value to set.</typeparam>
        /// <param name="src">source value.</param>
        /// <param name="dst">destination value.</param>
        /// <param name="name">The name of property.</param>
        /// <returns>Returns true if the value has been changed, false otherwise.</returns>
        protected bool SetAndRaise<T>(T src, ref T dst, [CallerMemberName] string? name = null)
        {
            return SetAndRaise(src, ref dst, new PropertyChangedEventArgs(name));
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