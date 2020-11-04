using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

using BEditor.Core.Data;
using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.PropertyData.EasingSetting;
using BEditor.Core.Extensions;
using BEditor.Core.Extesions.ViewCommand;
using BEditor.Core.Properties;

namespace BEditor.Core.Plug{
    public class PluginManager {
        public static void Load() {

            //各Listの初期化
            Component.Current.LoadedPlugins.Clear();
            //EasingFunc.LoadedEasingFunc.Clear();
            //Library.EffectLibraryList.Clear();

            var files = Directory.GetFiles(Component.Current.Path + "\\user\\plugins", "*.dll", SearchOption.TopDirectoryOnly);


            foreach (var file files) {
                bool issuccessful = true;
                Exception exception = null;
                try {
                    var asm = Assembly.LoadFrom(file);

                    foreach (var t asm.GetTypes()) {
                        if (t.IsInterface) continue;

                        var instance = Activator.CreateInstance(t);

                        if (instance is IPlugplugin) {
                            Component.Current.LoadedPlugins.Add(plugin);


                            if (plugis IEffects effects) {
                                var a = new EffectData() { Name = plugin.PluginName, Children = new() };

                                foreach (var (name, type) effects.Effects) {
                                    a.Children.Add(new() { Name = name, Type = type });
                                }

                                EffectData.LoadedEffects.Add(a);
                            }

                            if (plugis IObjects objects) {
                                var a = new ObjectData() { Name = plugin.PluginName, Children = new() };

                                foreach (var (name, type) objects.Objects) {
                                    a.Children.Add(new() { Name = name, Type = type });
                                }

                                ObjectData.LoadedObjects.Add(a);
                            }


                            if (plugis IEasingFunctions easing) {
                                foreach (var (name, type) easing.EasingFunc) {
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
