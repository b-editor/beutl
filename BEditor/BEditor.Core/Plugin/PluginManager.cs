using System;
using System.Collections.Generic;
using System.IO;
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
        /// <summary>
        /// すべてのDllがロードされたあとに発生します
        /// </summary>
        public static event EventHandler PluginsLoaded;

        /// <summary>
        /// 一つのDllが読み込まれたあとに発生します
        /// </summary>
        public static event EventHandler<PluginLoadedEventArgs> PluginLoaded;


        public static List<IPlugin> Load()
        {
            //EasingFunc.LoadedEasingFunc.Clear();
            //Library.EffectLibraryList.Clear();
            var files = Directory.GetFiles($"{Services.Path}\\user\\plugins", "*.dll", SearchOption.TopDirectoryOnly);

            var list = new List<IPlugin>();

            foreach (var file in files)
            {
                bool issuccessful = true;
                Exception exception = null;
                try
                {
                    var asm = Assembly.LoadFrom(file);

                    foreach (var t in asm.GetTypes())
                    {
                        if (t.IsInterface) continue;

                        var instance = Activator.CreateInstance(t);

                        if (instance is IPlugin plugin)
                        {
                            list.Add(plugin);


                            if (plugin is IEffects effects)
                            {
                                var a = new EffectMetadata() { Name = plugin.PluginName, Children = new() };

                                foreach (var metadata in effects.Effects)
                                {
                                    a.Children.Add(metadata);
                                    Serialize.SerializeKnownTypes.Add(metadata.Type);
                                }

                                EffectMetadata.LoadedEffects.Add(a);
                            }

                            if (plugin is IObjects objects)
                            {
                                var a = new ObjectMetadata() { Name = plugin.PluginName, Children = new() };

                                foreach (var metadata in objects.Objects)
                                {
                                    a.Children.Add(metadata);
                                    Serialize.SerializeKnownTypes.Add(metadata.Type);
                                }

                                ObjectMetadata.LoadedObjects.Add(a);
                            }


                            if (plugin is IEasingFunctions easing)
                            {
                                foreach (var data in easing.EasingFunc)
                                {
                                    EasingFunc.LoadedEasingFunc.Add(data);
                                    Serialize.SerializeKnownTypes.Add(data.Type);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    issuccessful = false;
                    exception = e;

                    Message.Snackbar(string.Format(Resources.FailedToLoad, Path.GetFileNameWithoutExtension(file)));
                    ActivityLog.ErrorLog(e);
                }

                PluginLoaded?.Invoke(null, new PluginLoadedEventArgs(file, issuccessful, exception));
            }

            PluginsLoaded?.Invoke(null, EventArgs.Empty);

            return list;
        }
    }
}
