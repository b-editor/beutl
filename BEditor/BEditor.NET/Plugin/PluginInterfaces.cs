
using System;
using System.Collections.Generic;

namespace BEditor.NET.Plugin {
    public interface IPlugin {
        /// <summary>
        /// プラグインの名前
        /// </summary>
        public string PluginName { get; }

        /// <summary>
        /// プラグインの説明
        /// </summary>
        public string Infomation { get; }

        /// <summary>
        /// プラグインの設定を開くときに呼び出されます
        /// </summary>
        public void SettingCommand();
    }

    public interface IEasingFunctions {
        /// <summary>
        /// B_Editor.Models.Datas.PropertyData.EasingSetting.EasingFuncを継承しているクラスのTypeと名前が入ったList
        /// </summary>
        public List<(string, Type)> EasingFunc { get; }
    }

    public interface IEffects {
        /// <summary>
        /// B_Editor.Models.Datas.EffectData.EffectElementを継承しているクラスのTypeと名前が入ったList
        /// </summary>
        public List<(string, Type)> Effects { get; }
    }

    public interface IObjects {
        /// <summary>
        /// B_Editor.Models.Datas.ObjectData.ObjectElement又はB_Editor.Models.Datas.ObjectData.DefaultData.DefaultImageObjectを継承しているクラスのTypeと名前が入ったList
        /// </summary>
        public List<(string, Type)> Objects { get; }
    }
}
