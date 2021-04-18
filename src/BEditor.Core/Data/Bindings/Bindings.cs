using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Resources;

namespace BEditor.Data.Bindings
{
    /// <summary>
    /// Represents a class that provides methods for objects that implement <see cref="IBindable{T}"/>.
    /// </summary>
    public static class Bindings
    {
        // Todo
        public static bool GetBindable<T>(this IBindable<T> bindable, Guid? id, out IBindable<T>? result)
        {

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