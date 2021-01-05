using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;

using BEditor.Core.Data;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Extensions;
using BEditor.Core.Extensions.ViewCommand;
using BEditor.Core.Properties;
using BEditor.Core.Service;

namespace BEditor.Core.Plugin
{
    public class PluginManager
    {
        private static readonly string pluginsDir = Path.Combine(AppContext.BaseDirectory, "user", "plugins");

        /// <summary>
        /// すべてのプラグイン名を取得
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> GetNames()
        {
            return Directory.GetDirectories(pluginsDir)
                .Select(static folder => Path.GetFileName(folder));
        }

        // 許可されたプラグインのリストを読み込む
        public static IEnumerable<IPlugin> Load(IEnumerable<string> pluginName)
        {
            var plugins = pluginName
                .Where(static f => f is not null)
                .Select(static f => Path.Combine(AppContext.BaseDirectory, "user", "plugins", f, $"{f}.dll"))
                .Where(static f => File.Exists(f))
                .Select(static f => Assembly.LoadFrom(f).GetTypes())
                .Select(static types => types
                    .Select(static t => Activator.CreateInstance(t) as IPlugin)
                    .Where(static t => t is not null))
                .SelectMany(static f => f);

            foreach (var plugin in plugins)
            {
                if (plugin is IEffects effects)
                {
                    var a = new EffectMetadata() { Name = plugin.PluginName, Children = effects.Effects };

                    Serialize.SerializeKnownTypes.AddRange(effects.Effects.Select(a => a.Type));

                    EffectMetadata.LoadedEffects.Add(a);
                }

                if (plugin is IObjects objects)
                {
                    Serialize.SerializeKnownTypes.AddRange(objects.Objects.Select(a => a.Type));

                    foreach(var o in objects.Objects)
                    {
                        ObjectMetadata.LoadedObjects.Add(o);
                    }
                }

                if (plugin is IEasingFunctions easing)
                {
                    foreach (var data in easing.EasingFunc)
                    {
                        EasingFunc.LoadedEasingFunc.Add(data);
                        Serialize.SerializeKnownTypes.Add(data.Type);
                    }
                }

                yield return plugin;
            }
        }
    }
}
