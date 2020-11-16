using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using BEditor.Models;
using BEditor.ViewModels.ToolControl;
using BEditor.Views.ToolControl.Default;
using BEditor.ObjectModel;
using BEditor.ObjectModel.EffectData;
using BEditor.ObjectModel.ObjectData;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// Object_Setting.xaml の相互作用ロジック
    /// </summary>
    public partial class Object_Setting : UserControl
    {
        public Object_Setting(ClipData data)
        {
            InitializeComponent();

            DataContext = Data = data;
        }

        public ClipData Data { get; set; }


        private void Preview_Drop(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            Type datatype = typeof(EffectData);
            EffectData effect;

            try
            {
                effect = (EffectData)e.Data.GetData(datatype);
            }
            catch
            {
                return;
            }


            EffectElement effectinstance = (EffectElement)Activator.CreateInstance(effect.Type);

            UndoRedoManager.Do(new EffectElement.AddCommand(effectinstance, Data));
        }
    }
}
