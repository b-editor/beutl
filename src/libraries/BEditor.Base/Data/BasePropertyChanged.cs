// BasePropertyChanged.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
            PropertyChanged?.Invoke(this, args);
        }
    }
}