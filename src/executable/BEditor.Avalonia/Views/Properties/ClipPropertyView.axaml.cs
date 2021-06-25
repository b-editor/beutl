using System.IO;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;

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
            e.DragEffects = (e.Data.Contains("EffectMetadata") || (e.Data.GetFileNames()?.Any(f => Path.GetExtension(f) is ".befct") ?? false)) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void UserControl_Drop(object? sender, DragEventArgs e)
        {
            if (DataContext is not ClipElement clip) return;

            if (e.Data.Get("EffectMetadata") is EffectMetadata metadata)
            {
                clip.AddEffect(metadata.CreateFunc()).Execute();
            }
            else if (e.Data.GetFileNames()?.FirstOrDefault(f => Path.GetExtension(f) is ".befct") is var file && file is not null && File.Exists(file))
            {
                var efct = await Serialize.LoadFromFileAsync<EffectWrapper>(file);

                if (efct?.Effect is null)
                {
                    AppModel.Current.Message.Snackbar(string.Format(Strings.FailedToLoad, Strings.EffectFile));
                }
                else
                {
                    efct.Effect.Load();
                    efct.Effect.UpdateId();
                    clip.AddEffect(efct.Effect).Execute();
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}