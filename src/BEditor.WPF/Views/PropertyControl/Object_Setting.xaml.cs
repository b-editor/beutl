using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using BEditor.Command;
using BEditor.Data;
using BEditor.Models;
using BEditor.Views.ToolControl.Default;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// Object_Setting.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class Object_Setting : UserControl, IDisposable
    {
        public Object_Setting(ClipElement data)
        {
            DataContext = Data = data;

            InitializeComponent();
        }

        public ClipElement Data { get; set; }

        public void Dispose()
        {
            Data = null!;
            DataContext = null;

            GC.SuppressFinalize(this);
        }

        private void UserControl_Drop(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            Type datatype = typeof(EffectMetadata);

            try
            {
                var effect = (EffectMetadata)e.Data.GetData(datatype) ?? throw new Exception();

                var effectinstance = effect.CreateFunc();

                Data.AddEffect(effectinstance).Execute();
            }
            catch
            {
                return;
            }
        }

        private void UserControl_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = (e.Data.GetData(typeof(EffectMetadata)) is null) ? DragDropEffects.None : DragDropEffects.Copy;
        }
    }
}
