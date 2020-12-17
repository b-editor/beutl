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
using BEditor.Core.Command;

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
            Type datatype = typeof(EffectMetadata);

            try
            {
                var effect = (EffectMetadata)e.Data.GetData(datatype) ?? throw new Exception();


                var effectinstance = effect.CreateFunc?.Invoke() ?? Activator.CreateInstance(effect.Type) as EffectElement;

                CommandManager.Do(new EffectElement.AddCommand(effectinstance, Data));
            }
            catch
            {
                return;
            }
        }
    }
}
