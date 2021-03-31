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
        /// <summary>
        /// Get an <see cref="IBindable{T}"/> from a string that can be obtained by <see cref="PropertyElement.ToString(string?, IFormatProvider?)"/>.
        /// </summary>
        /// <typeparam name="T">Type of object to bind.</typeparam>
        /// <exception cref="DataException">Parent element not found.</exception>
        /// <exception cref="DataException">Child elements not found.</exception>
        /// <exception cref="DataException">Failed to convert value.</exception>
        /// <returns>Returns a <see cref="bool"/> indicating whether it was retrieved or not, <see langword="true"/> on success, <see langword="false"/> on failure.</returns>
        public static bool GetBindable<T>(this IBindable<T> bindable, string? str, out IBindable<T>? result)
        {
            if (str is null)
            {
                result = null;
                return false;
            }

            // Scene.Clip[Effect][Property]の場合
            var regex1 = new Regex(@"^([\da-zA-Z亜-熙ぁ-んァ-ヶ]+)\.([\da-zA-Z]+)\[([\d]+)\]\[([\d]+)\]\z");

            // Scene.Clip[Effect][Group][Property]の場合
            var regex2 = new Regex(@"^([\da-zA-Z亜-熙ぁ-んァ-ヶ]+)\.([\da-zA-Z]+)\[([\d]+)\]\[([\d]+)\]\[([\d]+)\]\z");

            if (regex1.IsMatch(str))
            {
                var match = regex1.Match(str);

                var proj = bindable.GetParent4() ?? throw new DataException(Strings.ParentElementNotFound);

                var scene = proj.Find(match.Groups[1].Value) ??
                    throw new DataException(Strings.ChildElementsNotFound);

                var clip = scene.Find(match.Groups[2].Value) ??
                    throw new DataException(Strings.ChildElementsNotFound);

                var effect = (int.TryParse(match.Groups[3].Value, out var id) ? clip.Find(id) :
                    throw new DataException(Strings.FailedToConvertValue)) ??
                    throw new DataException(Strings.ChildElementsNotFound);

                result = int.TryParse(match.Groups[4].Value, out var id1) ?
                    effect.Find(id1) as IBindable<T> :
                    throw new DataException(Strings.ChildElementsNotFound);

                return true;
            }
            else if (regex2.IsMatch(str))
            {
                var match = regex2.Match(str);

                var proj = bindable.GetParent4() ?? throw new DataException(Strings.ParentElementNotFound);

                var scene = proj.Find(match.Groups[1].Value) ??
                    throw new DataException(Strings.ChildElementsNotFound);

                var clip = scene.Find(match.Groups[2].Value) ??
                    throw new DataException(Strings.ChildElementsNotFound);

                var effect = (int.TryParse(match.Groups[3].Value, out var id) ? clip.Find(id) : throw new DataException(
                    Strings.FailedToConvertValue)) ??
                    throw new DataException(Strings.ChildElementsNotFound);

                var parent = int.TryParse(match.Groups[4].Value, out var id1) ?
                    (effect.Find(id1) as IParent<PropertyElement> ??
                        throw new DataException(Strings.ChildElementsNotFound)) :
                    throw new DataException(Strings.FailedToConvertValue);

                result = int.TryParse(match.Groups[5].Value, out var id2) ?
                    parent.Find(id2) as IBindable<T> :
                    throw new DataException(Strings.ChildElementsNotFound);

                return true;
            }

            result = null;
            return false;
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
                this._bindable = bindable;
                bindable.GetBindable(bindable.TargetHint, out _twoway);
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
