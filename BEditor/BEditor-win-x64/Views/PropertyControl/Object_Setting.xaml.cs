using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using BEditor.Models;
using BEditor.ViewModels.ToolControl;
using BEditor.Views.ToolControl.Default;
using BEditor.Core.Data;
using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;

namespace BEditor.Views.PropertyControls {
    /// <summary>
    /// Object_Setting.xaml の相互作用ロジック
    /// </summary>
    public partial class Object_Setting : UserControl {
        public Object_Setting(ClipData data) {
            InitializeComponent();

            DataContext = Data = data;
        }

        public ClipData Data { get; set; }


        private void Preview_Drop(object sender, DragEventArgs e) {
            e.Effects = DragDropEffects.Copy;
            Type datatype = typeof(EffectData);
            EffectData effect;

            try {
                effect = (EffectData)e.Data.GetData(datatype);
            }
            catch {
                return;
            }


            EffectElement effectinstance = (EffectElement)Activator.CreateInstance(effect.Type);
            effectinstance.ClipData = Data;
            UndoRedoManager.Do(new EffectElement.AddEffect(effectinstance));
        }
    }
}
