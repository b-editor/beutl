using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Extensions;

namespace BEditor.Core.Bindings
{
    public static class Bindings
    {
        public static bool GetBindable<T>(this IBindable<T> bindable, string text, out IBindable<T> result)
        {
            // Scene.Clip[Effect][Property]の場合
            var regex1 = new Regex(@"^([\da-zA-Z]+)\.([\da-zA-Z]+)\[([\d]+)\]\[([\d]+)\]\z");
            // Scene.Clip[Effect][Group][Property]の場合
            var regex2 = new Regex(@"^([\da-zA-Z]+)\.([\da-zA-Z]+)\[([\d]+)\]\[([\d]+)\]\[([\d]+)\]\z");

            if (regex1.IsMatch(text))
            {
                var match = regex1.Match(text);

                var scene = bindable.GetParent4().Find(match.Groups[1].Value);
                var clip = scene.Find(match.Groups[2].Value);
                var effect = (int.TryParse(match.Groups[3].Value, out var id) ? clip.Find(id) : throw new Exception()) ?? throw new Exception();
                result = int.TryParse(match.Groups[4].Value, out var id1) ? effect.Find(id1) as IBindable<T> : throw new Exception();

                return true;
            }
            else if (regex2.IsMatch(text))
            {
                var match = regex2.Match(text);

                var scene = bindable.GetParent4().Find(match.Groups[1].Value);
                var clip = scene.Find(match.Groups[2].Value);
                var effect = (int.TryParse(match.Groups[3].Value, out var id) ? clip.Find(id) : throw new Exception()) ?? throw new Exception();
                var parent = int.TryParse(match.Groups[4].Value, out var id1) ? effect.Find(id1) as IParent<PropertyElement> : throw new Exception();
                result = int.TryParse(match.Groups[5].Value, out var id2) ? parent.Find(id2) as IBindable<T> : throw new Exception();

                return true;
            }

            result = null;
            return false;
        }

        public static string GetString<T>(this IBindable<T> bindable)
        {
            if (bindable is PropertyElement p && bindable.Id == -1)
            {
                // bindable の親がGroup
                // bindable のIdは-1
                var scene = bindable.GetParent3().SceneName;
                var clip = bindable.GetParent2().Name;
                var effect = bindable.GetParent().Id;
                int group = -1;
                int property = -1;
                // エフェクトのChildrenからIParentのプロパティを見つける
                // ここが-1
                // var property = bindable.Id;

                // EffectElementの子要素からIParentを見つける
                Parallel.ForEach(bindable.GetParent().Children, item =>
                {
                    if (item is Data.PropertyData.Group parent && parent.Contains(p))
                    {
                        group = parent.Id;
                        property = parent.Children.ToList().IndexOf(p);
                    }
                });

                return $"{scene}.{clip}[{effect}][{group}][{property}]";
            }

            return $"{bindable.GetParent3().SceneName}.{bindable.GetParent2().Name}[{bindable.GetParent().Id}][{bindable.Id}]";
        }
    }
}
