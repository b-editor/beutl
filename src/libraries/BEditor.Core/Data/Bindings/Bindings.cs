// Bindings.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Diagnostics.CodeAnalysis;

using BEditor.Command;
using BEditor.Resources;

namespace BEditor.Data.Bindings
{
    /// <summary>
    /// Represents a class that provides methods for objects that implement <see cref="IBindable{T}"/>.
    /// </summary>
    public static class Bindings
    {
        /// <summary>
        /// Get <see cref="IBindable{T}"/> from <see cref="IEditingObject.Id"/>.
        /// </summary>
        /// <param name="bindable">The object used to get the target <see cref="IBindable{T}"/>.</param>
        /// <param name="id">The Id of the <see cref="IBindable{T}"/> to be retrieved.</param>
        /// <param name="result">The instance of the <see cref="IBindable{T}"/> that was retrieved.</param>
        /// <typeparam name="T">Type of object to bind.</typeparam>
        /// <returns>Returns a <see cref="bool"/> indicating whether it was retrieved or not, <see langword="true"/> on success, <see langword="false"/> on failure.</returns>
        public static bool GetBindable<T>(this IBindable<T> bindable, Guid? id, [NotNullWhen(true)] out IBindable<T>? result)
        {
            if (id is null)
            {
                result = null;
                return false;
            }

            result = bindable.GetParent<Project>()?.FindAllChildren<IBindable<T>>((Guid)id);

            return result is not null;
        }

        /// <summary>
        /// Create a command to bind two objects implementing <see cref="IBindable{T}"/>.
        /// </summary>
        /// <typeparam name="T">Type of object to bind.</typeparam>
        /// <param name="src">Bind source object.</param>
        /// <param name="target">Bind destination object.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public static IRecordCommand Bind<T>(this IBindable<T>? src, IBindable<T>? target) =>
            new BindCommand<T>(src, target);

        /// <summary>
        /// Create a command to disconnect the binding.
        /// </summary>
        /// <typeparam name="T">Type of object to bind.</typeparam>
        /// <param name="bindable">An object that implements <see cref="IBindable{T}"/> to disconnect.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public static IRecordCommand Disconnect<T>(this IBindable<T> bindable) =>
            new DisconnectCommand<T>(bindable);

        private sealed class BindCommand<T> : IRecordCommand
        {
            private readonly IBindable<T>? _source;
            private readonly IBindable<T>? _target;

            public BindCommand(IBindable<T>? source, IBindable<T>? target)
            {
                _source = source;
                _target = target;
            }

            public string Name => Strings.BindCommand;

            public void Do()
            {
                _source?.Bind(_target);
                _target?.Bind(_source);
            }

            public void Redo()
            {
                Do();
            }

            public void Undo()
            {
                _source?.Bind(null);
                _target?.Bind(null);
            }
        }

        private sealed class DisconnectCommand<T> : IRecordCommand
        {
            private readonly IBindable<T> _bindable;
            private readonly IBindable<T>? _twoway;

            public DisconnectCommand(IBindable<T> bindable)
            {
                _bindable = bindable;
                bindable.GetBindable(bindable.TargetID, out _twoway);
            }

            public string Name => Strings.Disconnect;

            public void Do()
            {
                _bindable.Bind(null);
                _twoway?.Bind(null);
            }

            public void Redo()
            {
                Do();
            }

            public void Undo()
            {
                _bindable.Bind(_twoway);
                _twoway?.Bind(_bindable);
            }
        }
    }
}