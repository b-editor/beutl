using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using BEditor.Data;

namespace BEditor.Views.Properties
{
    public sealed class ClipPropertyView : UserControl
    {
        public ClipPropertyView()
        {
            InitializeComponent();
        }

        public ClipPropertyView(ClipElement clip)
        {
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, UserControl_DragOver);
            AddHandler(DragDrop.DropEvent, UserControl_Drop);
            DataContext = clip;
            InitializeComponent();
        }

        private void UserControl_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.Data.Contains("EffectMetadata") ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void UserControl_Drop(object? sender, DragEventArgs e)
        {
            if (e.Data.Get("EffectMetadata") is EffectMetadata metadata && DataContext is ClipElement clip)
            {
                clip.AddEffect(metadata.CreateFunc()).Execute();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}