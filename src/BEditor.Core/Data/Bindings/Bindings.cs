using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Bindings
{
    public static class Bindings
    {
        public static bool GetBindable<T>(this IBindable<T> bindable, string? text, out IBindable<T>? result)
        {
            if (text is null)
            {
                result = null;
                return false;
            }

            // Scene.Clip[Effect][Property]の場合
            var regex1 = new Regex(@"^([\da-zA-Z亜-熙ぁ-んァ-ヶ]+)\.([\da-zA-Z]+)\[([\d]+)\]\[([\d]+)\]\z");
            // Scene.Clip[Effect][Group][Property]の場合
            var regex2 = new Regex(@"^([\da-zA-Z亜-熙ぁ-んァ-ヶ]+)\.([\da-zA-Z]+)\[([\d]+)\]\[([\d]+)\]\[([\d]+)\]\z");

            if (regex1.IsMatch(text))
            {
                var match = regex1.Match(text);

                var proj = bindable.GetParent4() ?? throw new Exception();
                var scene = proj.Find(match.Groups[1].Value) ?? throw new Exception();
                var clip = scene.Find(match.Groups[2].Value) ?? throw new Exception();
                var effect = (int.TryParse(match.Groups[3].Value, out var id) ? clip.Find(id) : throw new Exception()) ?? throw new Exception();
                result = int.TryParse(match.Groups[4].Value, out var id1) ? effect.Find(id1) as IBindable<T> : throw new Exception();

                return true;
            }
            else if (regex2.IsMatch(text))
            {
                var match = regex2.Match(text);

                var proj = bindable.GetParent4() ?? throw new Exception();
                var scene = proj.Find(match.Groups[1].Value) ?? throw new Exception();
                var clip = scene.Find(match.Groups[2].Value) ?? throw new Exception();
                var effect = (int.TryParse(match.Groups[3].Value, out var id) ? clip.Find(id) : throw new Exception()) ?? throw new Exception();
                var parent = int.TryParse(match.Groups[4].Value, out var id1) ? (effect.Find(id1) as IParent<PropertyElement> ?? throw new Exception()) : throw new Exception();
                result = int.TryParse(match.Groups[5].Value, out var id2) ? parent.Find(id2) as IBindable<T> : throw new Exception();

                return true;
            }

            result = null;
            return false;
        }

        public static string GetString(this IBindable bindable)
        {
            //[DoesNotReturnAttribute]
            //static void ParentNullThrow()
            //{
            //    throw new Exception();
            //}

            if (bindable is PropertyElement p && bindable.Id == -1)
            {
                // bindable の親がGroup
                // bindable のIdは-1
                var scene = bindable.GetParent3()?.SceneName ?? throw new Exception();
                var clip = bindable.GetParent2()?.Name ?? throw new Exception();
                var effect = bindable.GetParent()?.Id ?? throw new Exception();
                int group = -1;
                int property = -1;
                // エフェクトのChildrenからIParentのプロパティを見つける
                // ここが-1
                // var property = bindable.Id;

                // EffectElementの子要素からIParentを見つける
                Parallel.ForEach(bindable.GetParent()?.Children ?? throw new Exception(), item =>
                {
                    if (item is Property.Group parent && parent.Contains(p))
                    {
                        group = parent.Id;
                        property = parent.Children.ToList().IndexOf(p);
                    }
                });

                return $"{scene}.{clip}[{effect}][{group}][{property}]";
            }

            return $"{bindable.GetParent3()?.SceneName}.{bindable.GetParent2()?.Name}[{bindable.GetParent()?.Id}][{bindable.Id}]";
        }

        public sealed class BindCommand<T> : IRecordCommand
        {
            private readonly IBindable<T>? _Source;
            private readonly IBindable<T>? _Target;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="source">バインド先のオブジェクト</param>
            /// <param name="target">バインドするオブジェクト</param>
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
        public sealed class Disconnect<T> : IRecordCommand
        {
            private readonly IBindable<T> bindable;
            private readonly IBindable<T>? twoway;

            public Disconnect(IBindable<T> bindable)
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
