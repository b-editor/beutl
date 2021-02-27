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
using BEditor.Properties;

namespace BEditor.Data.Bindings
{
    /// <summary>
    /// Represents a class that provides methods for objects that implement <see cref="IBindable{T}"/>.
    /// </summary>
    public static class Bindings
    {
        /// <summary>
        /// Get an <see cref="IBindable{T}"/> from a string that can be obtained by <see cref="GetString(IBindable)"/>.
        /// </summary>
        /// <typeparam name="T">Type of object to bind</typeparam>
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
                
                var proj = bindable.GetParent4() ?? throw new DataException(ExceptionMessage.ParentElementNotFound);
                var scene = proj.Find(match.Groups[1].Value) ?? throw new DataException(ExceptionMessage.ChildElementsNotFound);
                var clip = scene.Find(match.Groups[2].Value) ?? throw new DataException(ExceptionMessage.ChildElementsNotFound);
                var effect = (int.TryParse(match.Groups[3].Value, out var id) ? clip.Find(id) : throw new DataException(ExceptionMessage.FailedToConvertValue)) ?? throw new DataException(ExceptionMessage.ChildElementsNotFound);
                result = int.TryParse(match.Groups[4].Value, out var id1) ? effect.Find(id1) as IBindable<T> : throw new DataException(ExceptionMessage.ChildElementsNotFound);

                return true;
            }
            else if (regex2.IsMatch(str))
            {
                var match = regex2.Match(str);

                var proj = bindable.GetParent4() ?? throw new DataException(ExceptionMessage.ParentElementNotFound);
                var scene = proj.Find(match.Groups[1].Value) ?? throw new DataException(ExceptionMessage.ChildElementsNotFound);
                var clip = scene.Find(match.Groups[2].Value) ?? throw new DataException(ExceptionMessage.ChildElementsNotFound);
                var effect = (int.TryParse(match.Groups[3].Value, out var id) ? clip.Find(id) : throw new DataException(ExceptionMessage.FailedToConvertValue)) ?? throw new DataException(ExceptionMessage.ChildElementsNotFound);
                var parent = int.TryParse(match.Groups[4].Value, out var id1) ? (effect.Find(id1) as IParent<PropertyElement> ?? throw new DataException(ExceptionMessage.ChildElementsNotFound)) : throw new DataException(ExceptionMessage.FailedToConvertValue);
                result = int.TryParse(match.Groups[5].Value, out var id2) ? parent.Find(id2) as IBindable<T> : throw new DataException(ExceptionMessage.ChildElementsNotFound);

                return true;
            }

            result = null;
            return false;
        }
        /// <summary>
        /// Get a <see cref="string"/> to retrieve an <see cref="IBindable"/>
        /// </summary>
        /// <param name="bindable">An object that implements <see cref="IBindable"/> to get a string.</param>
        /// <exception cref="DataException">Parent element not found.</exception>
        /// <returns>String that can be used to get an <see cref="IBindable"/> from <see cref="GetBindable{T}(IBindable{T}, string?, out IBindable{T}?)"/>.</returns>
        public static string GetString(this IBindable bindable)
        {
            if (bindable is PropertyElement p && bindable.Id == -1)
            {
                // bindable の親がGroup
                // bindable のIdは-1
                var scene = bindable.GetParent3()?.Name ?? throw new DataException(ExceptionMessage.ParentElementNotFound);
                var clip = bindable.GetParent2()?.Name ?? throw new DataException(ExceptionMessage.ParentElementNotFound);
                var effect = bindable.GetParent()?.Id ?? throw new DataException(ExceptionMessage.ParentElementNotFound);
                int group = -1;
                int property = -1;
                // エフェクトのChildrenからIParentのプロパティを見つける
                // ここが-1
                // var property = bindable.Id;

                // EffectElementの子要素からIParentを見つける
                Parallel.ForEach(bindable.GetParent()?.Children ?? throw new DataException(ExceptionMessage.ParentElementNotFound), item =>
                {
                    if (item is Property.Group parent && parent.Contains(p))
                    {
                        group = parent.Id;
                        property = parent.Children.ToList().IndexOf(p);
                    }
                });

                return $"{scene}.{clip}[{effect}][{group}][{property}]";
            }

            return $"{bindable.GetParent3()?.Name}.{bindable.GetParent2()?.Name}[{bindable.GetParent()?.Id}][{bindable.Id}]";
        }
        /// <summary>
        /// Create a command to bind two objects implementing <see cref="IBindable{T}"/>.
        /// </summary>
        /// <typeparam name="T">Type of object to bind</typeparam>
        /// <param name="src">Bind source object</param>
        /// <param name="target">Bind destination object</param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        public static IRecordCommand Bind<T>(this IBindable<T>? src, IBindable<T>? target)
            => new BindCommand<T>(src, target);
        /// <summary>
        /// Create a command to disconnect the binding
        /// </summary>
        /// <typeparam name="T">Type of object to bind</typeparam>
        /// <param name="bindable">An object that implements <see cref="IBindable{T}"/> to disconnect.</param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        public static IRecordCommand Disconnect<T>(this IBindable<T> bindable)
            => new DisconnectCommand<T>(bindable);

        private sealed class BindCommand<T> : IRecordCommand
        {
            private readonly IBindable<T>? _Source;
            private readonly IBindable<T>? _Target;

            public BindCommand(IBindable<T>? source, IBindable<T>? target)
            {
                _Source = source;
                _Target = target;
            }

            public string Name => CommandName.BindCommand;

            // target変更時にsourceが変更
            // targetを観察

            public void Do()
            {
                _Source?.Bind(_Target);
                _Target?.Bind(_Source);
            }
            public void Redo() => Do();
            public void Undo()
            {
                _Source?.Bind(null);
                _Target?.Bind(null);
            }
        }
        private sealed class DisconnectCommand<T> : IRecordCommand
        {
            private readonly IBindable<T> bindable;
            private readonly IBindable<T>? twoway;

            public DisconnectCommand(IBindable<T> bindable)
            {
                this.bindable = bindable;
                bindable.GetBindable(bindable.BindHint, out twoway);
            }

            public void Do()
            {
                bindable.Bind(null);
                twoway?.Bind(null);
            }
            public void Redo() => Do();
            public void Undo()
            {
                bindable.Bind(twoway);
                twoway?.Bind(bindable);
            }
        }
    }
}
