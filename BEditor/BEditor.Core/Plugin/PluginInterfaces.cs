
using System;
using System.Collections.Generic;

using BEditor.Core.Data;
using BEditor.Core.Data.Property.EasingProperty;

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
        /// プラグインの設定を開くときに呼び出されます
        /// </summary>
        public void SettingCommand();
    }

    public interface IEasingFunctions
    {
        public IEnumerable<EasingData> EasingFunc { get; }
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
