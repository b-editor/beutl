using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

using BEditor.NET.Data;
using BEditor.NET.Data.EffectData;
using BEditor.NET.Data.ObjectData;
using BEditor.NET.Data.PropertyData.EasingSetting;
using BEditor.NET.Extensions;
using BEditor.NET.Extesions.ViewCommand;
using BEditor.NET.Properties;

namespace BEditor.NET.Plugin {
    public class PluginManager {
        public static void Load() {

            //各Listの初期化
            Component.Current.LoadedPlugins.Clear();
            //EasingFunc.LoadedEasingFunc.Clear();
            //Library.EffectLibraryList.Clear();

            var files = Directory.GetFiles(Component.Current.Path + "\\user\\plugins", "*.dll", SearchOption.TopDirectoryOnly);


            foreach (var file in files) {
                bool issuccessful = true;
                Exception exception = null;
                try {
                    var asm = Assembly.LoadFrom(file);

                    foreach (var t in asm.GetTypes()) {
                        if (t.IsInterface) continue;

                        var instance = Activator.CreateInstance(t);

                        if (instance is IPlugin plugin) {
                            Component.Current.LoadedPlugins.Add(plugin);


                            if (plugin is IEffects effects) {
                                var a = new EffectData() { Name = plugin.PluginName, Children = new() };

                                foreach (var (name, type) in effects.Effects) {
                                    a.Children.Add(new() { Name = name, Type = type });
                                }

                                EffectData.LoadedEffects.Add(a);
                            }

                            if (plugin is IObjects objects) {
                                var a = new ObjectData() { Name = plugin.PluginName, Children = new() };

                                foreach (var (name, type) in objects.Objects) {
                                    a.Children.Add(new() { Name = name, Type = type });
                                }

                                ObjectData.LoadedObjects.Add(a);
                            }


                            if (plugin is IEasingFunctions easing) {
                                foreach (var (name, type) in easing.EasingFunc) {
                                    EasingFunc.LoadedEasingFunc.Add(new EasingData() { Name = name, Type = type });
                                }
                            }
                        }
                    }
                }
                catch (Exception e) {
                    issuccessful = false;
                    exception = e;

                    Message.Snackbar(string.Format(Resources.FailedToLoad, Path.GetFileNameWithoutExtension(file)));
                    ActivityLog.ErrorLog(e);
                }

                PluginLoaded?.Invoke(null, new PluginLoadedEventArgs(file, issuccessful, exception));
            }

            PluginsLoaded?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// すべてのDllがロードされたあとに発生します
        /// </summary>
        public static event EventHandler PluginsLoaded;

        /// <summary>
        /// 一つのDllが読み込まれたあとに発生します
        /// </summary>
        public static event EventHandler<PluginLoadedEventArgs> PluginLoaded;
    }
}
