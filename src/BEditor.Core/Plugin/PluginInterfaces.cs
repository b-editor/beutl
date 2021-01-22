
using System;
using System.Collections.Generic;

using BEditor.Core.Data;
using BEditor.Core.Data.Property.Easing;

namespace BEditor.Core.Plugin
{
    public interface IPlugin
    {
        /// <summary>
        /// プラグインの名前
        /// </summary>
        public string PluginName { get; }

        /// <summary>
        /// プラグインの説明
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// アセンブリの名前
        /// </summary>
        public sealed string AssemblyName => GetType().Assembly.GetName().Name!;

        /// <summary>
        /// プラグインの設定を開くときに呼び出されます
        /// </summary>
        public SettingRecord Settings { get; set; }
    }

    public interface IEasingFunctions
    {
        public IEnumerable<EasingMetadata> EasingFunc { get; }
    }

    public interface IEffects
    {
        public IEnumerable<EffectMetadata> Effects { get; }
    }

    public interface IObjects
    {
        public IEnumerable<ObjectMetadata> Objects { get; }
    }
}
